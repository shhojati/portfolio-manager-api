using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;
using PortfolioManager.Api.Tsetmc;

namespace PortfolioManager.Api.Ime;

/// <summary>
/// Background job that imports the commodity exchange's continuous deposit certificates listed in
/// <see cref="ImeCertificates"/> (gold bar, gold coin and silver bar) as Certificate <see cref="Asset"/>s, the same type
/// the TSETMC job gives the exchange's other certificates, and keeps one <see cref="Price"/> per asset per trading day
/// up to date with the day's settlement price, in Rial per certificate.
/// </summary>
public sealed class ImePriceSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<ImeOptions> options,
    ILogger<ImePriceSyncService> logger) : BackgroundService
{
    private const string CertificateType = TsetmcPriceSyncService.CertificateType;

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("IME certificate price sync is disabled");
            return;
        }

        logger.LogInformation("IME certificate price sync service started: every {Interval}, {LookbackDays} days back", opts.Interval, opts.LookbackDays);
        using var timer = new PeriodicTimer(opts.Interval);

        try
        {
            do
            {
                var runStart = TimeProvider.System.GetTimestamp();
                try
                {
                    await SyncPricesAsync(opts, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // IME being down, unreachable from this server or changing its API must not kill the job; try again next tick.
                    logger.LogError(ex, "IME sync run failed after {Elapsed}", TimeProvider.System.GetElapsedTime(runStart));
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            logger.LogInformation("IME certificate price sync service stopped");
        }
    }

    private async Task SyncPricesAsync(ImeOptions opts, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ImeClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var start = TimeProvider.System.GetTimestamp();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran));
        var from = today.AddDays(-Math.Max(0, opts.LookbackDays));

        // Each price is dated by the trading day it is for (see ImeCertificates.DailyPrices).
        var prices = new List<(ImeCertificate Certificate, decimal Value, DateTime Date)>();
        int failed = 0;
        foreach (var certificate in ImeCertificates.All)
        {
            IReadOnlyList<CertificateTrade> trades;
            try
            {
                trades = await client.GetCertificateTradesAsync(certificate.Code, certificate.Site, from, today, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failed++;
                logger.LogWarning(ex, "IME request for {Code} failed", certificate.Code);
                continue;
            }

            prices.AddRange(ImeCertificates.DailyPrices(certificate, trades)
                .Select(p => (certificate, p.Value, p.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))));
        }

        if (failed == ImeCertificates.All.Count)
            throw new HttpRequestException("Every IME request failed; IME's servers may only answer Iranian IPs");

        // Assets are matched by their identifier (the contract code), which never changes, so symbols, names and units
        // can be changed in ImeCertificates and existing assets follow on the next run, keeping their prices.
        var allAssets = await db.Assets.ToListAsync(ct);
        var imported = allAssets.Where(a => a.Type == CertificateType)
            .GroupBy(a => a.Identifier)
            .ToDictionary(g => g.Key, g => g.First());
        var symbols = allAssets.Select(a => a.Symbol).ToHashSet();
        var identifiers = allAssets.Select(a => a.Identifier).ToHashSet();

        var dates = prices.Select(p => p.Date).Distinct().ToList();
        var assetIds = imported.Values.Select(a => a.Id).ToList();
        var dayPrices = (await db.Prices.Where(p => dates.Contains(p.Date) && assetIds.Contains(p.AssetId)).ToListAsync(ct))
            .GroupBy(p => (p.AssetId, p.Date))
            .ToDictionary(g => g.Key, g => g.First());

        int createdAssets = 0, renamedAssets = 0, createdPrices = 0, skipped = 0;
        foreach (var (certificate, value, date) in prices)
        {
            if (!imported.TryGetValue(certificate.Code, out var asset))
            {
                if (symbols.Contains(certificate.Symbol) || !identifiers.Add(certificate.Code))
                {
                    // Another asset (e.g. one added by hand) already uses this symbol or identifier.
                    skipped++;
                    logger.LogWarning("Skipping {Code}: symbol {Symbol} or identifier {Code} is already taken", certificate.Code, certificate.Symbol, certificate.Code);
                    continue;
                }

                symbols.Add(certificate.Symbol);
                asset = new Asset { Symbol = certificate.Symbol, Identifier = certificate.Code, Name = certificate.Name, Type = CertificateType, Unit = certificate.Unit };
                db.Assets.Add(asset);
                imported[certificate.Code] = asset;
                createdAssets++;
            }
            else if (asset.Symbol != certificate.Symbol || asset.Name != certificate.Name || asset.Unit != certificate.Unit)
            {
                renamedAssets += Rename(asset, certificate, symbols) ? 1 : 0;
            }

            // One row per asset per trading day, updated in place as IME settles the day.
            if (asset.Id != 0 && dayPrices.TryGetValue((asset.Id, date), out var price))
            {
                price.Value = value;
            }
            else
            {
                db.Prices.Add(new Price { Asset = asset, Value = value, Date = date });
                createdPrices++;
            }
        }

        var updatedPrices = db.ChangeTracker.Entries<Price>().Count(e => e.State == EntityState.Modified);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "IME certificate price sync finished in {Elapsed} for {From:yyyy-MM-dd}..{To:yyyy-MM-dd}: {Prices} daily prices received, {CreatedAssets} assets added, {RenamedAssets} renamed, {CreatedPrices} prices added, {UpdatedPrices} updated, {Skipped} skipped (symbol taken), {Failed} requests failed",
            TimeProvider.System.GetElapsedTime(start), from, today, prices.Count, createdAssets, renamedAssets, createdPrices, updatedPrices, skipped, failed);
    }

    // Brings an existing asset's symbol, name and unit in line with ImeCertificates. The symbol is only changed when
    // no other asset uses it, since symbols are unique. Returns whether anything changed.
    private bool Rename(Asset asset, ImeCertificate certificate, HashSet<string> symbols)
    {
        var renamed = asset.Name != certificate.Name || asset.Unit != certificate.Unit;
        asset.Name = certificate.Name;
        asset.Unit = certificate.Unit;

        if (asset.Symbol != certificate.Symbol)
        {
            if (symbols.Add(certificate.Symbol))
            {
                symbols.Remove(asset.Symbol);
                asset.Symbol = certificate.Symbol;
                renamed = true;
            }
            else
            {
                logger.LogWarning("Not changing the symbol of {Code} to {Symbol}: the symbol is already taken", certificate.Code, certificate.Symbol);
            }
        }

        return renamed;
    }
}
