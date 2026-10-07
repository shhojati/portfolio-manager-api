using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Tsetmc;

/// <summary>
/// Background job that imports every stock and ETF listed on TSETMC (Tehran Stock Exchange, Farabourse, and the
/// commodity and energy exchanges) as an <see cref="Asset"/> and keeps one <see cref="Price"/> per asset per trading
/// day up to date (closing price, plus redemption NAV for ETFs). Once a day it also discovers the ones the market watch
/// leaves out, such as suspended instruments.
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
        DateOnly? lastDiscovery = null;
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

                    // Last, since its first run is slow; if it fails, it is tried again next run.
                    var today = DateOnly.FromDateTime(now);
                    if (syncPrices && opts.DiscoverInstruments && lastDiscovery != today)
                    {
                        await DiscoverInstrumentsAsync(opts, stoppingToken);
                        lastDiscovery = today;
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
            if (string.IsNullOrEmpty(item.InsCode) || Classify(item.Isin) is not { } type || item.ClosingPrice <= 0)
                continue;

            var (asset, created) = GetOrAddAsset(db, assets, symbols, item, type);
            if (asset is null)
            {
                skipped++;
                continue;
            }

            createdAssets += created ? 1 : 0;

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

    /// <summary>
    /// The market watch only has instruments active that day, so a stock or fund suspended (or not traded for months)
    /// since before it was first imported would never be: e.g. نور, a project fund that hasn't traded since listing.
    /// This looks up every instrument TSETMC knows of that the market watch left out, each only once (the result is
    /// kept in <see cref="AppDbContext.TsetmcInstruments"/>), and imports the stocks and funds among them with their
    /// last closing price, dated by the day they last traded.
    /// </summary>
    private async Task DiscoverInstrumentsAsync(TsetmcOptions opts, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<TsetmcClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        logger.LogInformation("TSETMC instrument discovery started");
        var start = TimeProvider.System.GetTimestamp();

        var marketWatch = (await client.GetMarketWatchAsync(ct)).Select(i => i.InsCode).OfType<string>().ToHashSet();
        var codes = (await client.GetAllInsCodesAsync(ct))
            .Concat(opts.ExtraInsCodes.Select(c => c.Trim()).Where(c => c.Length > 0))
            .Where(c => !marketWatch.Contains(c))
            .ToHashSet();

        // Look up the instruments not seen before, and keep them before going on, so a failure later doesn't repeat it.
        var known = await db.TsetmcInstruments.ToDictionaryAsync(i => i.InsCode, ct);
        var looked = new ConcurrentBag<TsetmcInstrument>();
        var lookupsFailed = await ForEachWithRetriesAsync(codes.Where(c => !known.ContainsKey(c)), opts.DiscoveryConcurrency,
            async (insCode, token) =>
            {
                var item = await client.GetInstrumentInfoAsync(insCode, token);
                looked.Add(item is null
                    ? new TsetmcInstrument { InsCode = insCode }
                    : new TsetmcInstrument
                    {
                        InsCode = insCode,
                        Isin = item.Isin,
                        Symbol = Truncate(Normalize(item.Symbol), 100),
                        Name = Truncate(Normalize(item.Name), 100),
                    });
            }, ct);
        db.TsetmcInstruments.AddRange(looked);
        await db.SaveChangesAsync(ct);
        foreach (var instrument in looked)
            known[instrument.InsCode] = instrument;

        // Their prices don't change while they're suspended, but one may have traded since it was last seen. Many have
        // no price at all: registered but never traded (TSETMC shows a placeholder 1,000), or ones TSETMC always answers
        // with a 500 (e.g. IRO3Z... Farabourse instruments). Those are tried once, not three times, to keep runs short.
        var importable = codes.Select(c => known.GetValueOrDefault(c))
            .Where(i => i is not null && Classify(i.Isin) is not null)
            .Select(i => i!)
            .ToList();
        var closings = new ConcurrentDictionary<string, ClosingPrice>();
        await ForEachWithRetriesAsync(importable.Select(i => i.InsCode), opts.DiscoveryConcurrency,
            async (insCode, token) =>
            {
                if (await client.GetClosingPriceAsync(insCode, token) is { } closing)
                    closings[insCode] = closing;
            }, ct, attempts: 1);

        var assets = await db.Assets.ToDictionaryAsync(a => a.Identifier, ct);
        var symbols = assets.Values.Select(a => a.Symbol).ToHashSet();
        var dates = closings.Values.Select(c => ToPriceDate(c.LastTradeDate)).Distinct().ToList();
        var assetIds = assets.Values.Select(a => a.Id).ToList();
        var existingPrices = (await db.Prices.Where(p => dates.Contains(p.Date) && assetIds.Contains(p.AssetId)).ToListAsync(ct))
            .GroupBy(p => (p.AssetId, p.Date))
            .ToDictionary(g => g.Key, g => g.First());

        int createdAssets = 0, createdPrices = 0, skipped = 0;
        foreach (var instrument in importable)
        {
            if (!closings.TryGetValue(instrument.InsCode, out var closing))
                continue;

            var item = new MarketWatchItem(instrument.InsCode, instrument.Isin, instrument.Symbol, instrument.Name, closing.Value);
            var (asset, created) = GetOrAddAsset(db, assets, symbols, item, Classify(instrument.Isin)!);
            if (asset is null)
            {
                skipped++;
                continue;
            }

            createdAssets += created ? 1 : 0;
            var date = ToPriceDate(closing.LastTradeDate);
            if (asset.Id != 0 && existingPrices.TryGetValue((asset.Id, date), out var price))
            {
                price.Value = closing.Value;
            }
            else
            {
                db.Prices.Add(new Price { Asset = asset, Value = closing.Value, Date = date });
                createdPrices++;
            }
        }

        var updatedPrices = db.ChangeTracker.Entries<Price>().Count(e => e.State == EntityState.Modified);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "TSETMC instrument discovery finished in {Elapsed}: {Codes} instruments outside the market watch, {LookedUp} looked up ({LookupsFailed} failed), {Importable} stocks and funds ({NoPrice} without a price), {CreatedAssets} assets added, {CreatedPrices} prices added, {UpdatedPrices} updated, {Skipped} skipped (symbol taken)",
            TimeProvider.System.GetElapsedTime(start), codes.Count, looked.Count, lookupsFailed, importable.Count,
            importable.Count - closings.Count, createdAssets, createdPrices, updatedPrices, skipped);
    }

    // The asset an instrument is imported as, added if it is new; null if another asset (e.g. one added by hand)
    // already uses its symbol. Keeps the asset's TSETMC code up to date, which the NAV sync relies on.
    private (Asset? Asset, bool Created) GetOrAddAsset(
        AppDbContext db, Dictionary<string, Asset> assets, HashSet<string> symbols, MarketWatchItem item, string type)
    {
        var isin = item.Isin!;
        var created = false;
        if (!assets.TryGetValue(isin, out var asset))
        {
            var symbol = Normalize(item.Symbol).ToUpperInvariant();
            if (symbol.Length is 0 or > 100 || !symbols.Add(symbol))
            {
                logger.LogDebug("Skipping {Isin}: symbol '{Symbol}' is already taken", isin, symbol);
                return (null, false);
            }

            asset = new Asset { Symbol = symbol, Identifier = isin, Name = Truncate(Normalize(item.Name), 100), Type = type };
            db.Assets.Add(asset);
            assets[isin] = asset;
            created = true;
        }

        asset.TsetmcInsCode = item.InsCode;
        return (asset, created);
    }

    // Runs an action for every item, a few at a time. TSETMC sporadically answers 500/502 under load, and the same
    // request usually succeeds a moment later, so by default each one is tried up to three times. Returns how many
    // still failed.
    private async Task<int> ForEachWithRetriesAsync<T>(
        IEnumerable<T> items, int concurrency, Func<T, CancellationToken, Task> action, CancellationToken ct, int attempts = 3)
    {
        var failed = 0;
        await Parallel.ForEachAsync(items,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, concurrency), CancellationToken = ct },
            async (item, token) =>
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await action(item, token);
                        return;
                    }
                    catch (Exception) when (!token.IsCancellationRequested && attempt < attempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt), token);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref failed);
                        logger.LogDebug(ex, "TSETMC request for {Item} failed", item);
                        return;
                    }
                }
            });

        return failed;
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
        var failed = await ForEachWithRetriesAsync(etfs, opts.NavConcurrency,
            async (etf, token) =>
            {
                if (await client.GetEtfNavAsync(etf.InsCode, token) is { } nav)
                    navs[etf.Id] = nav;
            }, ct);

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

    /// <summary>
    /// The asset type an instrument is imported as, from its ISIN alone: ETFs are exchange-traded fund units (IRT),
    /// stocks are main shares on TSE/IFB/base market (IRO1/3/5/7). The sector code isn't used: it agrees for everything
    /// in the market watch, but TSETMC files some project funds under their project's industry instead of sector 68
    /// (پرند is in sector 40, electricity supply).
    /// </summary>
    private static string? Classify(string? isin)
    {
        // ISINs ending in 0001 are the primary instrument; other suffixes are rights, unit classes, etc.
        if (isin is not { Length: 12 } || !isin.EndsWith("0001", StringComparison.Ordinal))
            return null;
        if (isin.StartsWith("IRT", StringComparison.Ordinal))
            return EtfType;
        if (isin.StartsWith("IRO", StringComparison.Ordinal) && isin[3] is '1' or '3' or '5' or '7')
            return StockType;

        return null;
    }

    // Prices from this job are dated by trading day, stored as midnight UTC of that date.
    private static DateTime ToPriceDate(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    // TSETMC uses Arabic ي/ك; store Persian ی/ک so searches typed on a Persian keyboard match.
    private static string Normalize(string? text) => (text ?? string.Empty).Replace('ي', 'ی').Replace('ك', 'ک').Trim();

    private static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;
}
