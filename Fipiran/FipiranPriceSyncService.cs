using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Fipiran;

/// <summary>
/// Background job that imports every issuance/redemption mutual fund (صندوق صدور و ابطالی) listed on fipiran.ir as a
/// Fund <see cref="Asset"/> and keeps one <see cref="Price"/> per asset per NAV date up to date with its redemption NAV
/// (NAV ابطال), in Rial per unit. Exchange-traded funds are left to the TSETMC job, which imports them as ETFs.
/// </summary>
public sealed partial class FipiranPriceSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<FipiranOptions> options,
    ILogger<FipiranPriceSyncService> logger) : BackgroundService
{
    public const string FundType = "Fund";

    private const string IssuanceAndCancellation = "IssuanceAndCancellation";

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("FIPIRAN fund NAV sync is disabled");
            return;
        }

        logger.LogInformation("FIPIRAN fund NAV sync service started: every {Interval}", opts.Interval);
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
                    // FIPIRAN being down or changing its API must not kill the job; try again next tick.
                    logger.LogError(ex, "FIPIRAN sync run failed after {Elapsed}", TimeProvider.System.GetElapsedTime(runStart));
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            logger.LogInformation("FIPIRAN fund NAV sync service stopped");
        }
    }

    private async Task SyncPricesAsync(FipiranOptions opts, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<FipiranClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var start = TimeProvider.System.GetTimestamp();
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran).Date;
        var items = await client.GetFundsAsync(ct);

        // Each price is dated by the fund's NAV date, not the day we fetched it; funds publish a day or more late.
        int stale = 0;
        var funds = new List<(string Code, string Name, decimal Nav, DateTime Date)>();
        foreach (var item in items.Where(i => i.TypeOfInvest == IssuanceAndCancellation && !string.IsNullOrWhiteSpace(i.RegNo)).DistinctBy(i => i.RegNo))
        {
            if (item.CancelNav is not > 0 || item.Date is not { } navDate || today - navDate.Date > opts.MaxNavAge)
            {
                // Not yet started, stopped reporting, or liquidated.
                stale++;
                continue;
            }

            funds.Add((Code(item.RegNo!), Name(item), item.CancelNav.Value, DateTime.SpecifyKind(navDate.Date, DateTimeKind.Utc)));
        }

        var allAssets = await db.Assets.ToListAsync(ct);
        var imported = allAssets.Where(a => a.Type == FundType)
            .GroupBy(a => a.Identifier)
            .ToDictionary(g => g.Key, g => g.First());
        var symbols = allAssets.Select(a => a.Symbol).ToHashSet();
        var identifiers = allAssets.Select(a => a.Identifier).ToHashSet();

        var dates = funds.Select(f => f.Date).Distinct().ToList();
        var assetIds = imported.Values.Select(a => a.Id).ToList();
        var dayPrices = (await db.Prices.Where(p => dates.Contains(p.Date) && assetIds.Contains(p.AssetId)).ToListAsync(ct))
            .GroupBy(p => (p.AssetId, p.Date))
            .ToDictionary(g => g.Key, g => g.First());

        // Funds are matched by their identifier (the registration number), which never changes, so a fund renamed on
        // FIPIRAN, or a change to how names are cleaned, renames the existing asset on the next run, keeping its prices.
        int createdAssets = 0, renamedAssets = 0, createdPrices = 0, skipped = 0;
        foreach (var (code, name, nav, date) in funds)
        {
            if (!imported.TryGetValue(code, out var asset))
            {
                // The name doubles as the symbol, since funds have no ticker; if another asset already uses it as its
                // symbol (e.g. an ETF whose ticker is the same word), the registration code is the symbol instead.
                var symbol = symbols.Contains(name) ? code : name;
                if (!symbols.Add(symbol) || !identifiers.Add(code))
                {
                    // An asset of another type (e.g. one added by hand) already uses this symbol or identifier.
                    skipped++;
                    logger.LogDebug("Skipping {Code}: symbol or identifier is already taken", code);
                    continue;
                }

                asset = new Asset { Symbol = symbol, Identifier = code, Name = name, Type = FundType };
                db.Assets.Add(asset);
                imported[code] = asset;
                createdAssets++;
            }
            else if (asset.Symbol != name || asset.Name != name)
            {
                renamedAssets += Rename(asset, name, symbols) ? 1 : 0;
            }

            // One row per asset per NAV date. The redemption NAV is what a unit is worth to its holder, so it is the
            // price; Nav stays empty because, unlike an ETF's, there is no market price for it to differ from.
            if (asset.Id != 0 && dayPrices.TryGetValue((asset.Id, date), out var price))
            {
                price.Value = nav;
            }
            else
            {
                db.Prices.Add(new Price { Asset = asset, Value = nav, Date = date });
                createdPrices++;
            }
        }

        var updatedPrices = db.ChangeTracker.Entries<Price>().Count(e => e.State == EntityState.Modified);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "FIPIRAN fund NAV sync finished in {Elapsed}: {Funds} funds received, {CreatedAssets} assets added, {RenamedAssets} renamed, {CreatedPrices} prices added, {UpdatedPrices} updated, {Stale} stale, {Skipped} skipped (symbol taken)",
            TimeProvider.System.GetElapsedTime(start), funds.Count, createdAssets, renamedAssets, createdPrices, updatedPrices, stale, skipped);
    }

    // Brings an existing fund's name, and its symbol where that is free, in line with FIPIRAN.
    private static bool Rename(Asset asset, string name, HashSet<string> symbols)
    {
        var renamed = asset.Name != name;
        asset.Name = name;

        if (asset.Symbol != name && symbols.Add(name))
        {
            symbols.Remove(asset.Symbol);
            asset.Symbol = name;
            renamed = true;
        }

        return renamed;
    }

    // Funds have no ticker or ISIN, so the identifier comes from the SEO registration number, e.g. "FUND-10789".
    private static string Code(string regNo) => $"FUND-{regNo.Trim()}";

    // FIPIRAN uses Arabic ي/ك in some names; store Persian ی/ک so searches typed on a Persian keyboard match. Its names
    // mostly leave out «صندوق سرمایه‌گذاری» («مشترک پیشرو مفید»), but a few keep it or carry stray characters from the
    // site's markup («>صندوق سرمایه‌گذاری بازنشستگی تکمیلی اندوخته آگاه»), so both are stripped.
    private static string Name(FundItem item)
    {
        var name = (item.Name ?? string.Empty).Replace('ي', 'ی').Replace('ك', 'ک');
        name = Whitespace().Replace(name, " ");
        name = Edges().Replace(name, string.Empty);
        name = FundPrefix().Replace(name, string.Empty);
        if (name.Length == 0)
            name = item.RegNo!.Trim();

        return name.Length > 100 ? name[..100].TrimEnd() : name;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // Anything but a letter or digit at the start, or anything but a letter, digit or closing bracket at the end.
    [GeneratedRegex(@"^[^\p{L}\p{N}]+|[^\p{L}\p{N})]+$")]
    private static partial Regex Edges();

    // «صندوق سرمایه‌گذاری» (with a ZWNJ, a space or nothing between سرمایه and گذاری), or just «صندوق», at the start.
    [GeneratedRegex("^صندوق (سرمایه[\u200c ]?گذاری )?")]
    private static partial Regex FundPrefix();
}
