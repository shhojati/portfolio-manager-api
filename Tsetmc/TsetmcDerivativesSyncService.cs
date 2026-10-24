using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;
using static PortfolioManager.Api.Tsetmc.TsetmcImport;

namespace PortfolioManager.Api.Tsetmc;

/// <summary>
/// Background job that imports every TSE and IFB option (calls «ض», puts «ط») and stock future on TSETMC as an
/// <see cref="Asset"/> with its <see cref="DerivativeContract"/> terms, and keeps one <see cref="Price"/> per contract per
/// trading day up to date: the closing price, in Rial per unit of the underlying, as the exchange quotes it. It runs more
/// often than <see cref="TsetmcPriceSyncService"/>, which leaves these instruments out.
/// </summary>
public sealed class TsetmcDerivativesSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<TsetmcDerivativesOptions> options,
    ILogger<TsetmcDerivativesSyncService> logger) : BackgroundService
{
    public const string OptionType = "Option";
    public const string FutureType = "Future";

    // Prices are per share (or fund unit) of the underlying, not per contract. Also the IME option job's.
    public const string Unit = "واحد پایه";

    private static readonly PersianCalendar Persian = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("TSETMC derivatives sync is disabled");
            return;
        }

        logger.LogInformation("TSETMC derivatives sync service started: every {Interval}, {MarketOpen}-{MarketClose} (Tehran time)",
            opts.Interval, opts.MarketOpen, opts.MarketClose);

        var force = opts.RunOnStartup;
        using var timer = new PeriodicTimer(opts.Interval);

        try
        {
            do
            {
                var now = TehranNow();
                if (!force && !IsActive(opts.TradingDays, opts.MarketOpen, opts.MarketClose, now))
                {
                    // Every few minutes all night, so not worth more than a debug line.
                    logger.LogDebug("TSETMC derivatives sync skipped at {Now:yyyy-MM-dd HH:mm} (outside active hours)", now);
                    continue;
                }

                force = false;
                try
                {
                    await SyncAsync(opts, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // TSETMC being down or changing its API must not kill the job; try again next tick.
                    logger.LogError(ex, "TSETMC derivatives sync failed; next run in {Interval}", opts.Interval);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            logger.LogInformation("TSETMC derivatives sync service stopped");
        }
    }

    private async Task SyncAsync(TsetmcDerivativesOptions opts, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<TsetmcClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var start = TimeProvider.System.GetTimestamp();

        var tradingDay = await client.GetLastTradingDateAsync(ct) ?? DateOnly.FromDateTime(TehranNow());
        var date = ToPriceDate(tradingDay);
        var options = await client.GetOptionMarketWatchAsync(ct);
        // The option market watch has no ISINs, and futures are only in this one.
        var marketWatch = await client.GetMarketWatchAsync(ct);

        var assets = await db.Assets.Include(a => a.Contract).ToDictionaryAsync(a => a.Identifier, ct);
        var symbols = assets.Values.Select(a => a.Symbol).ToHashSet();
        var byInsCode = assets.Values.Where(a => a.TsetmcInsCode is not null)
            .GroupBy(a => a.TsetmcInsCode!)
            .ToDictionary(g => g.Key, g => g.First());
        var bySymbol = assets.Values.ToDictionary(a => a.Symbol);
        var dayPrices = (await db.Prices
                .Where(p => p.Date == date && (p.Asset!.Type == OptionType || p.Asset.Type == FutureType))
                .ToListAsync(ct))
            .GroupBy(p => p.AssetId)
            .ToDictionary(g => g.Key, g => g.First());

        var isins = marketWatch.Where(i => i.InsCode is not null && i.Isin is { Length: 12 })
            .GroupBy(i => i.InsCode!)
            .ToDictionary(g => g.Key, g => g.First().Isin!);
        var lookupsFailed = await FindMissingIsinsAsync(client, db, opts, options, byInsCode, isins, ct);

        var futures = marketWatch.Where(i => i.InsCode is not null && IsFuture(i.Isin)).ToList();
        var contractSizes = new ConcurrentDictionary<string, int>();
        lookupsFailed += await ForEachWithRetriesAsync(
            futures.Where(f => byInsCode.GetValueOrDefault(f.InsCode!)?.Contract is null).Select(f => f.InsCode!),
            opts.LookupConcurrency,
            async (insCode, token) =>
            {
                if (await client.GetContractSizeAsync(insCode, token) is { } size)
                    contractSizes[insCode] = size;
            }, logger, ct);

        int createdAssets = 0, createdPrices = 0, skipped = 0, unresolved = 0;

        void Import(MarketWatchItem item, string type, string kind, decimal? strike, DateOnly expiry, int contractSize,
            Asset? underlying, string underlyingSymbol)
        {
            if (!byInsCode.TryGetValue(item.InsCode!, out var asset))
            {
                // TSETMC reuses a series' symbol for a later year (ضهرم8030 can come back a year on), so if an older
                // contract holds the symbol, this one gets its expiry year appended.
                var symbol = Normalize(item.Symbol).ToUpperInvariant();
                if (!assets.ContainsKey(item.Isin!) && symbols.Contains(symbol))
                    item = item with { Symbol = $"{symbol}-{Persian.GetYear(expiry.ToDateTime(TimeOnly.MinValue))}" };

                (asset, var created) = GetOrAddAsset(db, assets, symbols, item, type, Unit, logger);
                if (asset is null)
                {
                    skipped++;
                    return;
                }

                createdAssets += created ? 1 : 0;
                byInsCode[item.InsCode!] = asset;
            }

            // Strikes and contract sizes are adjusted after the underlying pays a dividend or raises capital.
            var contract = asset.Contract ??= new DerivativeContract();
            contract.Kind = kind;
            contract.StrikePrice = strike;
            contract.ExpiryDate = expiry;
            contract.ContractSize = contractSize;
            contract.UnderlyingSymbol = Truncate(underlyingSymbol, 100);
            if (underlying is not null)
                contract.UnderlyingAsset = underlying;

            if (item.ClosingPrice <= 0)
                return;

            // One row per contract per trading day, updated in place during the session.
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

        foreach (var quote in options)
        {
            if (quote.InsCode is null)
                continue;
            if (!byInsCode.ContainsKey(quote.InsCode) && !isins.ContainsKey(quote.InsCode))
            {
                unresolved++;
                continue;
            }

            var underlyingSymbol = Normalize(quote.UnderlyingSymbol);
            var underlying = (quote.UnderlyingInsCode is null ? null : byInsCode.GetValueOrDefault(quote.UnderlyingInsCode))
                ?? bySymbol.GetValueOrDefault(underlyingSymbol.ToUpperInvariant());
            var item = new MarketWatchItem(quote.InsCode, byInsCode.GetValueOrDefault(quote.InsCode)?.Identifier ?? isins[quote.InsCode],
                quote.Symbol, quote.Name, quote.ClosingPrice);
            Import(item, OptionType, quote.IsCall ? DerivativeKinds.Call : DerivativeKinds.Put, quote.StrikePrice,
                quote.ExpiryDate, quote.ContractSize, underlying, underlyingSymbol);
        }

        foreach (var future in futures)
        {
            var size = byInsCode.GetValueOrDefault(future.InsCode!)?.Contract?.ContractSize ?? contractSizes.GetValueOrDefault(future.InsCode!);
            if (ParseFutureName(future.Name) is not { } terms || size <= 0)
            {
                unresolved++;
                continue;
            }

            var (underlyingSymbol, expiry) = terms;
            var underlying = bySymbol.GetValueOrDefault(underlyingSymbol.ToUpperInvariant());
            Import(future, FutureType, DerivativeKinds.Future, null, expiry, size, underlying, underlyingSymbol);
        }

        var updatedPrices = db.ChangeTracker.Entries<Price>().Count(e => e.State == EntityState.Modified);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "TSETMC derivatives sync finished in {Elapsed} for trading day {Date:yyyy-MM-dd}: {Options} options and {Futures} futures received, {CreatedAssets} assets added, {CreatedPrices} prices added, {UpdatedPrices} updated, {Skipped} skipped (symbol taken), {Unresolved} not resolved ({LookupsFailed} lookups failed)",
            TimeProvider.System.GetElapsedTime(start), tradingDay, options.Count, futures.Count, createdAssets, createdPrices, updatedPrices,
            skipped, unresolved, lookupsFailed);
    }

    /// <summary>
    /// The market watch only has the options traded today, so the ISIN of a new one that isn't is taken from what
    /// instrument discovery already looked up, or else looked up here. Returns how many lookups failed; those options are
    /// tried again next run.
    /// </summary>
    private async Task<int> FindMissingIsinsAsync(TsetmcClient client, AppDbContext db, TsetmcDerivativesOptions opts,
        IReadOnlyList<OptionQuote> options, Dictionary<string, Asset> byInsCode, Dictionary<string, string> isins, CancellationToken ct)
    {
        var missing = options.Select(o => o.InsCode).OfType<string>()
            .Where(c => !byInsCode.ContainsKey(c) && !isins.ContainsKey(c))
            .Distinct()
            .ToList();
        if (missing.Count == 0)
            return 0;

        var known = await db.TsetmcInstruments.Where(i => missing.Contains(i.InsCode) && i.Isin != null).ToListAsync(ct);
        foreach (var instrument in known.Where(i => i.Isin!.Length == 12))
            isins[instrument.InsCode] = instrument.Isin!;

        var looked = new ConcurrentDictionary<string, string>();
        var failed = await ForEachWithRetriesAsync(missing.Where(c => !isins.ContainsKey(c)), opts.LookupConcurrency,
            async (insCode, token) =>
            {
                if (await client.GetInstrumentInfoAsync(insCode, token) is { Isin.Length: 12 } info)
                    looked[insCode] = info.Isin!;
            }, logger, ct);
        foreach (var (insCode, isin) in looked)
            isins[insCode] = isin;

        return failed;
    }

    // Stock futures are TSE instruments of their own type: IRO4 ISINs.
    private static bool IsFuture(string? isin) => isin is { Length: 12 } && isin.StartsWith("IRO4", StringComparison.Ordinal);

    /// <summary>
    /// The underlying and expiry of a stock future, from its name: «آتي خودرو-1405/08/06» is on خودرو, expiring
    /// 1405/08/06 (TSETMC gives no other source for either). Null if the name isn't in that shape.
    /// </summary>
    private static (string Underlying, DateOnly Expiry)? ParseFutureName(string? name)
    {
        var text = Normalize(name);
        var dash = text.LastIndexOf('-');
        if (!text.StartsWith("آتی", StringComparison.Ordinal) || dash < 0)
            return null;

        var underlying = text[3..dash].Trim();
        var digits = text[(dash + 1)..].Replace("/", "").Trim();
        if (underlying.Length == 0 || digits.Length != 8 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var ymd))
            return null;

        try
        {
            return (underlying, DateOnly.FromDateTime(Persian.ToDateTime(ymd / 10000, ymd / 100 % 100, ymd % 100, 0, 0, 0, 0)));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
