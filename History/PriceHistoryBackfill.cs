using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Bitpin;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Fipiran;
using PortfolioManager.Api.Ime;
using PortfolioManager.Api.Models;
using PortfolioManager.Api.Tgju;
using PortfolioManager.Api.Tsetmc;

namespace PortfolioManager.Api.History;

/// <summary>An asset a history request asks for, with what's needed to tell whether its past prices are stored.</summary>
/// <param name="FirstPriceDate">The day of its first stored price; null if it has none.</param>
public sealed record HistoryAsset(int Id, string Identifier, string Type, string? TsetmcInsCode, DateTime? HistoryLoadedAt, DateTime? FirstPriceDate);

/// <summary>
/// Fetches past prices from the sources the price sync jobs use when a history request asks for days before what's
/// stored, and stores them like the sync jobs' own prices (one row per asset per day, in Rial), so each is fetched once.
/// <list type="bullet">
/// <item>TSETMC, Bitpin and tgju give an asset's whole daily history in one request, and IME a certificate's since it
/// started, so those are fetched per asset, all at once, the first time a day before its first stored price is asked
/// for; <see cref="Asset.HistoryLoadedAt"/> marks it as done. Only days before its first stored price are added, so
/// they never overlap with the sync job's.</item>
/// <item>FIPIRAN answers with every fund's NAV as of one day, so it is fetched per day, only the days asked for (one a
/// week over a range longer than two months), and each one is kept in <see cref="AppDbContext.FipiranDays"/>. The last
/// few days are left to the FIPIRAN sync job.</item>
/// </list>
/// Fetches run in the background, a few per source at a time, so they go on after a request stops waiting for them, and
/// requests for the same asset or day share one fetch. A failed one is tried again after
/// <see cref="PriceHistoryOptions.RetryAfter"/>. Past prices carry no NAV (an ETF's, or a coin's gold value).
/// </summary>
public sealed class PriceHistoryBackfill(
    IServiceScopeFactory scopeFactory,
    IOptions<PriceHistoryOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<PriceHistoryBackfill> logger)
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    // Funds publish a day or more late, so the FIPIRAN sync job, which dates each NAV by its own day, has these.
    private const int FipiranRecentDays = 3;

    // Over a longer range (a chart), FIPIRAN is asked for one day a week rather than every day: a fund's NAV moves
    // slowly, and a year then takes 52 requests rather than 365. Days asked for directly, like a trade's, are exact.
    private const int FipiranDailyRange = 62;
    private const int FipiranWeek = 7;

    // Fetches in progress, by asset or day, so concurrent requests share them.
    private readonly ConcurrentDictionary<string, Lazy<Task>> _running = new();

    // When each fetch that failed last failed.
    private readonly ConcurrentDictionary<string, DateTime> _failedAt = new();

    private readonly ConcurrentDictionary<PriceSource, SemaphoreSlim> _gates = new();

    /// <summary>
    /// Starts fetching whatever of <paramref name="assets"/>' prices from <paramref name="from"/> to <paramref name="to"/>
    /// isn't stored and can be fetched, and waits for it for up to <see cref="PriceHistoryOptions.WaitTimeout"/>.
    /// </summary>
    public async Task EnsureAsync(IReadOnlyList<HistoryAsset> assets, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var opts = options.Value;
        if (!opts.Enabled)
            return;

        var today = Today();
        var fetches = new List<Task>();
        foreach (var asset in assets)
        {
            var source = PriceSources.Of(asset.Type, asset.Identifier, asset.TsetmcInsCode);
            if (source is PriceSource.None or PriceSource.Fipiran || asset.HistoryLoadedAt is not null)
                continue;

            // Days from the first stored price on are the sync job's.
            var stored = asset.FirstPriceDate is { } first ? DateOnly.FromDateTime(first) : today;
            if (from < stored)
                fetches.Add(Run($"{source}:{asset.Id}", source, token => LoadAssetHistoryAsync(asset.Id, source, token)));
        }

        if (assets.Any(a => PriceSources.Of(a.Type, a.Identifier, a.TsetmcInsCode) == PriceSource.Fipiran))
        {
            var first = from > opts.EarliestDate ? from : opts.EarliestDate;
            var last = to < today.AddDays(-FipiranRecentDays) ? to : today.AddDays(-FipiranRecentDays);
            if (first <= last)
            {
                // Weekly days are on one grid, so every range shares them, and it starts on or before the range's
                // first day, so the range starts with a price.
                var weekly = last.DayNumber - first.DayNumber + 1 > FipiranDailyRange;
                if (weekly)
                    first = first.AddDays(-(first.DayNumber % FipiranWeek));

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var fetched = (await db.FipiranDays.Where(d => d.Date >= first && d.Date <= last).Select(d => d.Date).ToListAsync(ct)).ToHashSet();
                // The latest days first, since a request waits for the earliest ones the least.
                for (var day = last; day >= first; day = day.AddDays(-1))
                {
                    if (fetched.Contains(day) || (weekly && day != last && day.DayNumber % FipiranWeek != 0))
                        continue;
                    var d = day;
                    fetches.Add(Run($"{PriceSource.Fipiran}:{d:yyyy-MM-dd}", PriceSource.Fipiran, token => LoadFipiranDayAsync(d, token)));
                }
            }
        }

        if (fetches.Count == 0)
            return;

        try
        {
            await Task.WhenAll(fetches).WaitAsync(opts.WaitTimeout, ct);
        }
        catch (TimeoutException)
        {
            // Answer with what's stored; the rest is on its way.
        }
    }

    // Starts a fetch, or joins the one already running under the same key. Never throws: a failure is logged and the
    // key isn't tried again for a while.
    private Task Run(string key, PriceSource source, Func<CancellationToken, Task> fetch)
    {
        if (_failedAt.TryGetValue(key, out var failedAt) && DateTime.UtcNow - failedAt < options.Value.RetryAfter)
            return Task.CompletedTask;

        return _running.GetOrAdd(key, _ => new Lazy<Task>(() => Task.Run(() => ExecuteAsync(key, source, fetch)))).Value;
    }

    private async Task ExecuteAsync(string key, PriceSource source, Func<CancellationToken, Task> fetch)
    {
        // Not tied to the request that started it, only to the app shutting down.
        var token = lifetime.ApplicationStopping;
        var gate = _gates.GetOrAdd(source, s => new SemaphoreSlim(s == PriceSource.Fipiran ? 1 : Math.Max(1, options.Value.Concurrency)));
        try
        {
            await gate.WaitAsync(token);
            try
            {
                await fetch(token);
                _failedAt.TryRemove(key, out _);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _failedAt[key] = DateTime.UtcNow;
            logger.LogWarning(ex, "Fetching past prices for {Key} failed; it is tried again after {RetryAfter}", key, options.Value.RetryAfter);
        }
        catch (Exception)
        {
            // Shutting down.
        }
        finally
        {
            _running.TryRemove(key, out _);
        }
    }

    // Stores an asset's past prices from its source: every day before its first stored price, and not today.
    private async Task LoadAssetHistoryAsync(int assetId, PriceSource source, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        db.BroadcastChanges = false;

        // Checked again: a request that read the asset before another fetch of it finished may have started this one.
        var asset = await db.Assets.FindAsync([assetId], ct);
        if (asset is null || asset.HistoryLoadedAt is not null)
            return;

        var start = TimeProvider.System.GetTimestamp();
        var today = Today();
        IReadOnlyList<(DateOnly Date, decimal Value)> days = source switch
        {
            PriceSource.Tsetmc => (await services.GetRequiredService<TsetmcClient>().GetDailyClosingPricesAsync(asset.TsetmcInsCode!, ct))
                .Select(d => (d.LastTradeDate, d.Value)).ToList(),
            PriceSource.Bitpin => (await services.GetRequiredService<BitpinClient>()
                    .GetDailyClosesAsync(asset.Identifier + BitpinPriceSyncService.TomanSuffix, options.Value.EarliestDate, ct))
                .Select(d => (d.Date, d.Close * BitpinPriceSyncService.RialsPerToman)).ToList(),
            PriceSource.Tgju => await TgjuHistoryAsync(services.GetRequiredService<TgjuClient>(), asset, ct),
            PriceSource.Ime => await ImeHistoryAsync(services.GetRequiredService<ImeClient>(), asset, today, ct),
            _ => [],
        };

        var firstStored = await db.Prices.Where(p => p.AssetId == assetId).MinAsync(p => (DateTime?)p.Date, ct);
        var until = firstStored is { } f && DateOnly.FromDateTime(f) < today ? DateOnly.FromDateTime(f) : today;
        var added = 0;
        foreach (var (date, value) in days.Where(d => d.Date < until && d.Value > 0).DistinctBy(d => d.Date))
        {
            db.Prices.Add(new Price { AssetId = assetId, Value = value, Date = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) });
            added++;
        }

        asset.HistoryLoadedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Past prices of {Identifier} fetched from {Source} in {Elapsed}: {Received} days received, {Added} added (before {Until:yyyy-MM-dd})",
            asset.Identifier, source, TimeProvider.System.GetElapsedTime(start), days.Count, added, until);
    }

    // Converted like the tgju sync job's prices: per unit, and from dollars at that day's free-market dollar (or the
    // last one before it) for world metals.
    private static async Task<IReadOnlyList<(DateOnly, decimal)>> TgjuHistoryAsync(TgjuClient client, Asset asset, CancellationToken ct)
    {
        var key = TgjuInstruments.KeyOf(asset.Type, asset.Identifier)!;
        var instrument = TgjuInstruments.ByKey[key];
        var days = await client.GetDailyHistoryAsync(key, ct);
        if (!instrument.InUsd)
            return days.Select(d => (d.Date, d.Close / instrument.Units)).ToList();

        var usd = await client.GetDailyHistoryAsync(TgjuInstruments.UsdKey, ct);
        var result = new List<(DateOnly, decimal)>(days.Count);
        decimal? rate = null;
        var next = 0;
        foreach (var day in days)
        {
            while (next < usd.Count && usd[next].Date <= day.Date)
                rate = usd[next++].Close;
            if (rate is { } r)
                result.Add((day.Date, Math.Round(day.Close * r) / instrument.Units));
        }

        return result;
    }

    private static async Task<IReadOnlyList<(DateOnly, decimal)>> ImeHistoryAsync(ImeClient client, Asset asset, DateOnly today, CancellationToken ct)
    {
        var certificate = ImeCertificates.ByCode(asset.Identifier)!;
        var trades = await client.GetCertificateTradesAsync(certificate.Code, certificate.Site, ImeCertificates.HistoryStart, today, ct);
        return ImeCertificates.DailyPrices(certificate, trades).ToList();
    }

    // Stores every fund's NAV as of one day, dated by each fund's own NAV date, for the funds already imported.
    private async Task LoadFipiranDayAsync(DateOnly day, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BroadcastChanges = false;

        if (await db.FipiranDays.AnyAsync(d => d.Date == day, ct))
            return;

        var items = await scope.ServiceProvider.GetRequiredService<FipiranClient>().GetFundsAsync(day, ct);
        // Even a holiday has every fund's last NAV, so nothing at all means something went wrong; the day isn't kept.
        if (items.Count == 0)
            throw new InvalidOperationException($"FIPIRAN returned no funds as of {day:yyyy-MM-dd}");

        var funds = await db.Assets
            .Where(a => a.Type == FipiranPriceSyncService.FundType)
            .ToDictionaryAsync(a => a.Identifier, a => a.Id, ct);

        var navs = items
            .Where(i => i.TypeOfInvest == FipiranPriceSyncService.IssuanceAndCancellation && !string.IsNullOrWhiteSpace(i.RegNo)
                && i.CancelNav is > 0 && i.Date is not null)
            .Select(i => (Id: funds.GetValueOrDefault(FipiranPriceSyncService.Code(i.RegNo!)), Nav: i.CancelNav!.Value,
                Date: i.Date!.Value.Date))
            .Where(n => n.Id != 0 && DateOnly.FromDateTime(n.Date) <= day)
            .DistinctBy(n => n.Id)
            .ToList();

        var ids = navs.Select(n => n.Id).ToList();
        var dates = navs.Select(n => DateTime.SpecifyKind(n.Date, DateTimeKind.Utc)).Distinct().ToList();
        var stored = (await db.Prices.Where(p => ids.Contains(p.AssetId) && dates.Contains(p.Date)).Select(p => new { p.AssetId, p.Date }).ToListAsync(ct))
            .Select(p => (p.AssetId, p.Date.Date))
            .ToHashSet();

        var added = 0;
        foreach (var (id, nav, date) in navs)
        {
            if (!stored.Add((id, date)))
                continue;
            db.Prices.Add(new Price { AssetId = id, Value = nav, Date = DateTime.SpecifyKind(date, DateTimeKind.Utc) });
            added++;
        }

        db.FipiranDays.Add(new FipiranDay { Date = day });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Fund NAVs as of {Day:yyyy-MM-dd} fetched from FIPIRAN: {Received} funds received, {Added} prices added",
            day, navs.Count, added);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran));
}
