using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Tsetmc;

/// <summary>
/// Background job that imports every Tehran Stock Exchange / Farabourse stock and ETF as an
/// <see cref="Asset"/> and keeps one <see cref="Price"/> per asset per trading day up to date
/// (closing price, plus redemption NAV for ETFs).
/// </summary>
public sealed class TsetmcPriceSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<TsetmcOptions> options,
    ILogger<TsetmcPriceSyncService> logger) : BackgroundService
{
    public const string StockType = "Stock";
    public const string EtfType = "ETF";

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("TSETMC price sync is disabled");
            return;
        }

        logger.LogInformation(
            "TSETMC price sync service started: every {Interval}, prices {MarketOpen}-{MarketClose}, NAVs every {NavInterval} until {NavClose} (Tehran time)",
            opts.Interval, opts.MarketOpen, opts.MarketClose, opts.SyncEtfNav ? opts.NavInterval.ToString() : "never", opts.NavClose);

        var force = opts.RunOnStartup;
        DateTime? lastNavSync = null;
        using var timer = new PeriodicTimer(opts.Interval);

        try
        {
            do
            {
                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran);
                var next = now + opts.Interval;

                var syncPrices = force || IsActive(opts, now, opts.MarketClose);
                // Ticks drift by a few milliseconds, so allow half a tick of slack when checking whether NAVs are due.
                var syncNavs = opts.SyncEtfNav && (force ||
                    (IsActive(opts, now, opts.NavClose) && (lastNavSync is null || now - lastNavSync + opts.Interval / 2 >= opts.NavInterval)));
                force = false;

                if (!syncPrices && !syncNavs)
                {
                    logger.LogInformation("TSETMC sync skipped at {Now:yyyy-MM-dd HH:mm} (outside active hours); next run at {Next:yyyy-MM-dd HH:mm}", now, next);
                    continue;
                }

                logger.LogInformation("TSETMC sync run started at {Now:yyyy-MM-dd HH:mm:ss} (prices: {Prices}, NAVs: {Navs})", now, syncPrices, syncNavs);
                var runStart = TimeProvider.System.GetTimestamp();
                try
                {
                    if (syncPrices)
                        await SyncPricesAsync(stoppingToken);

                    if (syncNavs)
                    {
                        await SyncEtfNavsAsync(opts, stoppingToken);
                        lastNavSync = now;
                    }

                    logger.LogInformation("TSETMC sync run finished in {Elapsed}; next run at {Next:yyyy-MM-dd HH:mm}",
                        TimeProvider.System.GetElapsedTime(runStart), next);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // TSETMC being down or changing its API must not kill the job; try again next tick.
                    logger.LogError(ex, "TSETMC sync run failed after {Elapsed}; next run at {Next:yyyy-MM-dd HH:mm}",
                        TimeProvider.System.GetElapsedTime(runStart), next);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            logger.LogInformation("TSETMC price sync service stopped");
        }
    }

    private static bool IsActive(TsetmcOptions opts, DateTime tehranNow, TimeOnly until)
    {
        if (opts.TradingDays.Length > 0 && !opts.TradingDays.Contains(tehranNow.DayOfWeek))
            return false;

        var time = TimeOnly.FromDateTime(tehranNow);
        return time >= opts.MarketOpen && time <= until;
    }

    private async Task SyncPricesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<TsetmcClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        logger.LogInformation("TSETMC price sync started");
        var start = TimeProvider.System.GetTimestamp();

        var tradingDay = await client.GetLastTradingDateAsync(ct)
            ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran));
        var date = ToPriceDate(tradingDay);
        var items = await client.GetMarketWatchAsync(ct);

        var assets = await db.Assets.ToDictionaryAsync(a => a.Identifier, ct);
        var symbols = assets.Values.Select(a => a.Symbol).ToHashSet();
        var dayPrices = (await db.Prices.Where(p => p.Date == date).ToListAsync(ct))
            .GroupBy(p => p.AssetId)
            .ToDictionary(g => g.Key, g => g.First());

        int createdAssets = 0, createdPrices = 0, skipped = 0;
        foreach (var item in items)
        {
            if (Classify(item) is not { } type || item.ClosingPrice <= 0)
                continue;

            var isin = item.Isin!;
            if (!assets.TryGetValue(isin, out var asset))
            {
                var symbol = Normalize(item.Symbol).ToUpperInvariant();
                if (symbol.Length is 0 or > 20 || !symbols.Add(symbol))
                {
                    // Another asset (e.g. one added by hand) already uses this symbol.
                    skipped++;
                    logger.LogDebug("Skipping {Isin}: symbol '{Symbol}' is already taken", isin, symbol);
                    continue;
                }

                asset = new Asset { Symbol = symbol, Identifier = isin, Name = Normalize(item.Name), Type = type };
                db.Assets.Add(asset);
                assets[isin] = asset;
                createdAssets++;
            }

            asset.TsetmcInsCode = item.InsCode;

            // One row per asset per trading day, updated in place during the session.
            if (asset.Id != 0 && dayPrices.TryGetValue(asset.Id, out var price))
            {
                price.Value = item.ClosingPrice;
            }
            else
            {
                db.Prices.Add(new Price { Asset = asset, Value = item.ClosingPrice, Date = date });
                createdPrices++;
            }
        }

        var updatedPrices = db.ChangeTracker.Entries<Price>().Count(e => e.State == EntityState.Modified);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "TSETMC price sync finished in {Elapsed} for trading day {Date:yyyy-MM-dd}: {Instruments} instruments received, {CreatedAssets} assets added, {CreatedPrices} prices added, {UpdatedPrices} updated, {Skipped} skipped (symbol taken)",
            TimeProvider.System.GetElapsedTime(start), tradingDay, items.Count, createdAssets, createdPrices, updatedPrices, skipped);
    }

    private async Task SyncEtfNavsAsync(TsetmcOptions opts, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<TsetmcClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var etfs = await db.Assets
            .Where(a => a.Type == EtfType && a.TsetmcInsCode != null)
            .Select(a => new { a.Id, InsCode = a.TsetmcInsCode! })
            .ToListAsync(ct);

        logger.LogInformation("TSETMC NAV sync started for {Count} ETFs", etfs.Count);
        var start = TimeProvider.System.GetTimestamp();

        var navs = new ConcurrentDictionary<int, EtfNav>();
        var failed = 0;
        await Parallel.ForEachAsync(etfs,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, opts.NavConcurrency), CancellationToken = ct },
            async (etf, token) =>
            {
                // TSETMC sporadically answers 500/502 under load; the same request usually succeeds a moment later.
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (await client.GetEtfNavAsync(etf.InsCode, token) is { } nav)
                            navs[etf.Id] = nav;
                        return;
                    }
                    catch (Exception) when (!token.IsCancellationRequested && attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt), token);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref failed);
                        logger.LogDebug(ex, "Could not fetch NAV for asset {AssetId}", etf.Id);
                        return;
                    }
                }
            });

        // A NAV is only attached to the price row of the same trading day; it is never carried over to another day.
        var assetIds = navs.Keys.ToList();
        var dates = navs.Values.Select(n => ToPriceDate(n.Date)).Distinct().ToList();
        var prices = await db.Prices
            .Where(p => assetIds.Contains(p.AssetId) && dates.Contains(p.Date))
            .ToListAsync(ct);

        foreach (var price in prices)
        {
            if (navs.TryGetValue(price.AssetId, out var nav) && ToPriceDate(nav.Date) == price.Date)
                price.Nav = nav.Value;
        }

        var updated = db.ChangeTracker.Entries<Price>().Count(e => e.State == EntityState.Modified);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("TSETMC NAV sync finished in {Elapsed}: {Fetched}/{Total} fetched, {Updated} prices updated, {Failed} failed",
            TimeProvider.System.GetElapsedTime(start), navs.Count, etfs.Count, updated, failed);
    }

    /// <summary>Stocks are main shares on TSE/IFB/base market; ETFs are exchange-traded funds (sector 68).</summary>
    private static string? Classify(MarketWatchItem item)
    {
        // ISINs ending in 0001 are the primary instrument; other suffixes are rights, unit classes, etc.
        if (item.Isin is not { Length: 12 } isin || !isin.EndsWith("0001", StringComparison.Ordinal) || string.IsNullOrEmpty(item.InsCode))
            return null;

        var isFund = item.SectorCode?.Trim() == "68";
        if (isFund && isin.StartsWith("IRT", StringComparison.Ordinal))
            return EtfType;
        if (!isFund && isin.StartsWith("IRO", StringComparison.Ordinal) && isin[3] is '1' or '3' or '5' or '7')
            return StockType;

        return null;
    }

    // Prices from this job are dated by trading day, stored as midnight UTC of that date.
    private static DateTime ToPriceDate(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    // TSETMC uses Arabic ي/ك; store Persian ی/ک so searches typed on a Persian keyboard match.
    private static string Normalize(string? text) => (text ?? string.Empty).Replace('ي', 'ی').Replace('ك', 'ک').Trim();
}
