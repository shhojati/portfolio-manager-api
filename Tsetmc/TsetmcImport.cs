using Microsoft.Extensions.Logging;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Tsetmc;

/// <summary>What the TSETMC sync jobs (<see cref="TsetmcPriceSyncService"/>, <see cref="TsetmcDerivativesSyncService"/>) share.</summary>
internal static class TsetmcImport
{
    public static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static DateTime TehranNow() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran);

    /// <summary>Whether a job is active at a Tehran time: on one of its days (none means every day), between open and until.</summary>
    public static bool IsActive(DayOfWeek[] tradingDays, TimeOnly open, TimeOnly until, DateTime tehranNow)
    {
        if (tradingDays.Length > 0 && !tradingDays.Contains(tehranNow.DayOfWeek))
            return false;

        var time = TimeOnly.FromDateTime(tehranNow);
        return time >= open && time <= until;
    }

    /// <summary>
    /// The asset an instrument is imported as, added if it is new; null if another asset (e.g. one added by hand)
    /// already uses its symbol. Keeps the asset's TSETMC code up to date, which the NAV sync relies on.
    /// </summary>
    public static (Asset? Asset, bool Created) GetOrAddAsset(
        AppDbContext db, Dictionary<string, Asset> assets, HashSet<string> symbols, MarketWatchItem item, string type,
        string? unit, ILogger logger)
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

            asset = new Asset { Symbol = symbol, Identifier = isin, Name = Truncate(Normalize(item.Name), 100), Type = type, Unit = unit };
            db.Assets.Add(asset);
            assets[isin] = asset;
            created = true;
        }

        asset.TsetmcInsCode = item.InsCode;
        return (asset, created);
    }

    /// <summary>
    /// Runs an action for every item, a few at a time. TSETMC sporadically answers 500/502 under load, and the same
    /// request usually succeeds a moment later, so by default each one is tried up to three times. Returns how many
    /// still failed.
    /// </summary>
    public static async Task<int> ForEachWithRetriesAsync<T>(
        IEnumerable<T> items, int concurrency, Func<T, CancellationToken, Task> action, ILogger logger, CancellationToken ct,
        int attempts = 3)
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

    // Prices from these jobs are dated by trading day, stored as midnight UTC of that date.
    public static DateTime ToPriceDate(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    // TSETMC uses Arabic ي/ك; store Persian ی/ک so searches typed on a Persian keyboard match.
    public static string Normalize(string? text) => (text ?? string.Empty).Replace('ي', 'ی').Replace('ك', 'ک').Trim();

    public static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;
}
