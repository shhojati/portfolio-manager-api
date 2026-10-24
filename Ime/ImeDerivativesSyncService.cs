using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;
using PortfolioManager.Api.Tsetmc;
using static PortfolioManager.Api.Tsetmc.TsetmcImport;

namespace PortfolioManager.Api.Ime;

/// <summary>
/// Background job that imports the commodity exchange's open option contracts (on gold and silver bars and on gold and
/// silver funds) as Option <see cref="Asset"/>s, the same type the TSETMC derivatives job gives stock options, with
/// their <see cref="DerivativeContract"/> terms. It keeps one <see cref="Price"/> per contract per trading day: the
/// day's settlement price, in Rial per unit of the underlying. Only options whose underlying in
/// <see cref="ImeOptionUnderlyings"/> has a known contract size are imported. Each asset's identifier is IME's contract code.
/// </summary>
public sealed class ImeDerivativesSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<ImeDerivativesOptions> options,
    ILogger<ImeDerivativesSyncService> logger) : BackgroundService
{
    private const string OptionType = TsetmcDerivativesSyncService.OptionType;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("IME option sync is disabled");
            return;
        }

        logger.LogInformation("IME option sync service started: every {Interval}, {LookbackDays} days back", opts.Interval, opts.LookbackDays);
        using var timer = new PeriodicTimer(opts.Interval);

        try
        {
            do
            {
                var runStart = TimeProvider.System.GetTimestamp();
                try
                {
                    await SyncAsync(opts, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // IME being down, unreachable from this server or changing its API must not kill the job; try again next tick.
                    logger.LogError(ex, "IME option sync failed after {Elapsed}", TimeProvider.System.GetElapsedTime(runStart));
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            logger.LogInformation("IME option sync service stopped");
        }
    }

    private async Task SyncAsync(ImeDerivativesOptions opts, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ImeDerivativesClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var start = TimeProvider.System.GetTimestamp();
        var today = DateOnly.FromDateTime(TehranNow());
        var from = today.AddDays(-Math.Max(0, opts.LookbackDays));
        var days = await client.GetOptionDaysAsync(from, today, ct);

        var assets = await db.Assets.Include(a => a.Contract).ToListAsync(ct);
        // TSETMC's options have an insCode; IME's don't.
        var imported = assets.Where(a => a.Type == OptionType && a.TsetmcInsCode is null)
            .GroupBy(a => a.Identifier)
            .ToDictionary(g => g.Key, g => g.First());
        var symbols = assets.Select(a => a.Symbol).ToHashSet();
        var identifiers = assets.Select(a => a.Identifier).ToHashSet();
        var bySymbol = assets.ToDictionary(a => a.Symbol);

        var dates = days.Where(d => d.Date is not null).Select(d => ToPriceDate(DateOnly.FromDateTime(d.Date!.Value))).Distinct().ToList();
        var assetIds = imported.Values.Select(a => a.Id).ToList();
        var dayPrices = (await db.Prices.Where(p => dates.Contains(p.Date) && assetIds.Contains(p.AssetId)).ToListAsync(ct))
            .GroupBy(p => (p.AssetId, p.Date))
            .ToDictionary(g => g.Key, g => g.First());

        int createdAssets = 0, createdPrices = 0, skipped = 0, unknown = 0;
        var unknownUnderlyings = new SortedSet<string>();
        foreach (var day in days)
        {
            var code = day.ContractCode?.Trim() ?? string.Empty;
            if (code.Length == 0 || day.Date is null)
                continue;

            var underlying = ImeOptionUnderlyings.ByCode(code);
            var terms = ImeDerivativesClient.ParseTerms(day.ContractDescription);
            var expiry = ImeDerivativesClient.ParseJalali(day.DeliveryDate);
            if (underlying?.ContractSize is not { } contractSize || terms is null || expiry is null)
            {
                unknown++;
                unknownUnderlyings.Add(underlying?.Name ?? terms?.Underlying ?? code);
                continue;
            }

            if (!imported.TryGetValue(code, out var asset))
            {
                if (code.Length > 100 || symbols.Contains(code) || !identifiers.Add(code))
                {
                    // Another asset (e.g. one added by hand) already uses this code as its symbol or identifier.
                    skipped++;
                    continue;
                }

                symbols.Add(code);
                asset = new Asset
                {
                    Symbol = code, Identifier = code, Name = Truncate(Normalize(day.ContractDescription), 100), Type = OptionType,
                    Unit = TsetmcDerivativesSyncService.Unit,
                };
                db.Assets.Add(asset);
                imported[code] = asset;
                createdAssets++;
            }

            var contract = asset.Contract ??= new DerivativeContract();
            contract.Kind = terms.IsCall ? DerivativeKinds.Call : DerivativeKinds.Put;
            contract.StrikePrice = terms.StrikePrice;
            contract.ExpiryDate = expiry.Value;
            contract.ContractSize = contractSize;
            contract.UnderlyingSymbol = Truncate(underlying.UnderlyingSymbol ?? underlying.Name, 100);
            if (underlying.UnderlyingSymbol is { } symbol && bySymbol.TryGetValue(symbol, out var underlyingAsset))
                contract.UnderlyingAsset = underlyingAsset;

            // The settlement price, or the last trade's for a day IME hasn't settled yet.
            var value = day.TodaySettlementPrice is > 0 ? day.TodaySettlementPrice.Value : day.LastPrice ?? 0;
            if (value <= 0)
                continue;

            // One row per contract per trading day, updated in place as IME settles the day.
            var date = ToPriceDate(DateOnly.FromDateTime(day.Date.Value));
            if (asset.Id != 0 && dayPrices.TryGetValue((asset.Id, date), out var price))
            {
                price.Value = value;
            }
            else
            {
                var added = new Price { Asset = asset, Value = value, Date = date };
                db.Prices.Add(added);
                if (asset.Id != 0)
                    dayPrices[(asset.Id, date)] = added;
                createdPrices++;
            }
        }

        var updatedPrices = db.ChangeTracker.Entries<Price>().Count(e => e.State == EntityState.Modified);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "IME option sync finished in {Elapsed} for {From:yyyy-MM-dd}..{To:yyyy-MM-dd}: {Days} daily rows received, {CreatedAssets} assets added, {CreatedPrices} prices added, {UpdatedPrices} updated, {Skipped} skipped (symbol taken), {Unknown} skipped (contract size unknown: {Underlyings})",
            TimeProvider.System.GetElapsedTime(start), from, today, days.Count, createdAssets, createdPrices, updatedPrices, skipped,
            unknown, string.Join("، ", unknownUnderlyings));
    }
}
