using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;
using static PortfolioManager.Api.Tsetmc.TsetmcImport;

namespace PortfolioManager.Api.Tsetmc;

/// <summary>
/// Background job that imports every stock and ETF listed on TSETMC (Tehran Stock Exchange, Farabourse, and the
/// commodity and energy exchanges), and the commodity exchange's deposit certificates and parallel salaf contracts, as
/// an <see cref="Asset"/> and keeps one <see cref="Price"/> per asset per trading day up to date (closing price, plus
/// redemption NAV for ETFs), and the TSE's total index (see <see cref="TsetmcIndices"/>) likewise. Once a day it also
/// discovers the ones the market watch leaves out, such as suspended instruments.
/// </summary>
public sealed class TsetmcPriceSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<TsetmcOptions> options,
    ILogger<TsetmcPriceSyncService> logger) : BackgroundService
{
    public const string StockType = "Stock";
    public const string EtfType = "ETF";
    public const string CertificateType = "Certificate";
    public const string SalafType = "Salaf";

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
                var now = TehranNow();
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
                    {
                        await SyncPricesAsync(stoppingToken);
                        await SyncIndexAsync(stoppingToken);
                    }

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

    private static bool IsActive(TsetmcOptions opts, DateTime tehranNow, TimeOnly until) =>
        TsetmcImport.IsActive(opts.TradingDays, opts.MarketOpen, until, tehranNow);

    private async Task SyncPricesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<TsetmcClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        logger.LogInformation("TSETMC price sync started");
        var start = TimeProvider.System.GetTimestamp();

        var tradingDay = await client.GetLastTradingDateAsync(ct)
            ?? DateOnly.FromDateTime(TehranNow());
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

            var (asset, created) = GetOrAddAsset(db, assets, symbols, item, type, UnitOf(type, Normalize(item.Symbol)), logger);
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
    /// Keeps the total index's value for the trading day, like an instrument's closing price: added as an asset the first
    /// time, then one price per trading day, updated in place during the session. A failure is logged and left to the
    /// next run, so it doesn't hold up the rest.
    /// </summary>
    private async Task SyncIndexAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<TsetmcClient>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (await client.GetIndexValueAsync(TsetmcIndices.TotalInsCode, ct) is not { } value)
            {
                logger.LogWarning("TSETMC index sync: the total index is missing from TSETMC's index list");
                return;
            }

            var tradingDay = await client.GetLastTradingDateAsync(ct) ?? DateOnly.FromDateTime(TehranNow());
            var date = ToPriceDate(tradingDay);
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Identifier == TsetmcIndices.TotalIdentifier, ct);
            if (asset is null)
            {
                if (await db.Assets.AnyAsync(a => a.Symbol == TsetmcIndices.TotalSymbol, ct))
                {
                    logger.LogWarning("TSETMC index sync: symbol '{Symbol}' is already taken", TsetmcIndices.TotalSymbol);
                    return;
                }

                asset = new Asset
                {
                    Symbol = TsetmcIndices.TotalSymbol,
                    Identifier = TsetmcIndices.TotalIdentifier,
                    Name = TsetmcIndices.TotalName,
                    Type = TsetmcIndices.IndexType,
                    Unit = TsetmcIndices.Unit,
                };
                db.Assets.Add(asset);
            }

            asset.TsetmcInsCode = TsetmcIndices.TotalInsCode;
            var price = asset.Id == 0 ? null : await db.Prices.FirstOrDefaultAsync(p => p.AssetId == asset.Id && p.Date == date, ct);
            if (price is null)
                db.Prices.Add(new Price { Asset = asset, Value = value, Date = date });
            else
                price.Value = value;

            await db.SaveChangesAsync(ct);
            logger.LogInformation("TSETMC index sync: total index {Value} for trading day {Date:yyyy-MM-dd}", value, tradingDay);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "TSETMC index sync failed; it is tried again next run");
        }
    }

    /// <summary>
    /// The market watch only has instruments active that day, so a stock or fund suspended (or not traded for months)
    /// since before it was first imported would never be: e.g. نور, a project fund that hasn't traded since listing.
    /// This looks up every instrument TSETMC knows of that the market watch left out, each only once (the result is
    /// kept in <see cref="AppDbContext.TsetmcInstruments"/>), and imports the stocks, funds, certificates and salaf among them with their
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
            }, logger, ct);
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
            }, logger, ct, attempts: 1);

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

            var type = Classify(instrument.Isin)!;
            var item = new MarketWatchItem(instrument.InsCode, instrument.Isin, instrument.Symbol, instrument.Name, closing.Value);
            var (asset, created) = GetOrAddAsset(db, assets, symbols, item, type, UnitOf(type, Normalize(item.Symbol)), logger);
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
            "TSETMC instrument discovery finished in {Elapsed}: {Codes} instruments outside the market watch, {LookedUp} looked up ({LookupsFailed} failed), {Importable} stocks, funds, certificates and salaf ({NoPrice} without a price), {CreatedAssets} assets added, {CreatedPrices} prices added, {UpdatedPrices} updated, {Skipped} skipped (symbol taken)",
            TimeProvider.System.GetElapsedTime(start), codes.Count, looked.Count, lookupsFailed, importable.Count,
            importable.Count - closings.Count, createdAssets, createdPrices, updatedPrices, skipped);
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
            }, logger, ct);

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
    /// stocks are main shares on TSE/IFB/base market (IRO1/3/5/7), certificates are the commodity exchange's deposit
    /// certificates (IRK: coin, saffron, pistachio, glass...) and salaf its parallel salaf contracts (IRBK). The sector
    /// code isn't used: it agrees for everything in the market watch, but TSETMC files some project funds under their
    /// project's industry instead of sector 68 (پرند is in sector 40, electricity supply).
    /// </summary>
    private static string? Classify(string? isin)
    {
        if (isin is not { Length: 12 })
            return null;

        // Each certificate or salaf issue is an instrument of its own, so these ISINs carry no 0001 suffix.
        if (isin.StartsWith("IRK", StringComparison.Ordinal))
            return CertificateType;
        if (isin.StartsWith("IRBK", StringComparison.Ordinal))
            return SalafType;

        // Otherwise, ISINs ending in 0001 are the primary instrument; other suffixes are rights, unit classes, etc.
        if (!isin.EndsWith("0001", StringComparison.Ordinal))
            return null;
        if (isin.StartsWith("IRT", StringComparison.Ordinal))
            return EtfType;
        if (isin.StartsWith("IRO", StringComparison.Ordinal) && isin[3] is '1' or '3' or '5' or '7')
            return StockType;

        return null;
    }

    // What one price is for, where the type's default doesn't say: a coin certificate's price is for one coin. Other
    // certificates are left alone; their unit varies by commodity and TSETMC doesn't give it.
    private static string? UnitOf(string type, string symbol) =>
        type == CertificateType && symbol.StartsWith("سکه", StringComparison.Ordinal) ? "عدد" : null;
}
