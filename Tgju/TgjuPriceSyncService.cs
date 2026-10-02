using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Tgju;

/// <summary>
/// Background job that imports the currencies, gold, coins and metals listed in <see cref="TgjuInstruments"/> as
/// <see cref="Asset"/>s and keeps one <see cref="Price"/> per asset per day (Tehran calendar day) up to date with
/// tgju's free-market price. Prices are stored in Rial per unit (one currency unit, gram, mesghal, coin, ounce or tonne).
/// </summary>
public sealed class TgjuPriceSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<TgjuOptions> options,
    ILogger<TgjuPriceSyncService> logger) : BackgroundService
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("tgju price sync is disabled");
            return;
        }

        logger.LogInformation("tgju price sync service started: every {Interval}", opts.Interval);
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
                    // tgju being down or changing its feed must not kill the job; try again next tick.
                    logger.LogError(ex, "tgju sync run failed after {Elapsed}", TimeProvider.System.GetElapsedTime(runStart));
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            logger.LogInformation("tgju price sync service stopped");
        }
    }

    private async Task SyncPricesAsync(TgjuOptions opts, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<TgjuClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var start = TimeProvider.System.GetTimestamp();
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran);
        var feed = await client.GetQuotesAsync(ct);
        bool IsFresh(TgjuQuote quote) => now - quote.TehranTime <= opts.MaxQuoteAge;

        // World metals are quoted in dollars; they are converted at the free-market dollar rate of the same fetch.
        decimal? usdRate = feed.TryGetValue(TgjuInstruments.UsdKey, out var usd) && usd.Price > 0 && IsFresh(usd) ? usd.Price : null;

        // Each price is dated by the day tgju last updated it, not the day we fetched it, so a quote tgju
        // hasn't refreshed (weekends, thinly traded currencies) doesn't pass for today's price.
        int stale = 0;
        var quotes = new List<(TgjuInstrument Instrument, decimal Value, DateTime Date)>();
        foreach (var (key, quote) in feed)
        {
            if (!TgjuInstruments.ByKey.TryGetValue(key, out var instrument) || quote.Price <= 0) continue;
            if (!IsFresh(quote) || (instrument.InUsd && usdRate is null))
            {
                stale++;
                continue;
            }

            var value = instrument.InUsd ? Math.Round(quote.Price * usdRate!.Value) : quote.Price;
            quotes.Add((instrument, value / instrument.Units, DateTime.SpecifyKind(quote.TehranTime.Date, DateTimeKind.Utc)));
        }

        // Assets are matched by their identifier (the Latin code), which never changes, so symbols and names
        // can be changed in TgjuInstruments and existing assets follow on the next run, keeping their prices.
        var types = TgjuInstruments.ByKey.Values.Select(i => i.Type).ToHashSet();
        var allAssets = await db.Assets.ToListAsync(ct);
        var imported = allAssets.Where(a => types.Contains(a.Type))
            .GroupBy(a => (a.Type, a.Identifier))
            .ToDictionary(g => g.Key, g => g.First());
        var symbols = allAssets.Select(a => a.Symbol).ToHashSet();
        var identifiers = allAssets.Select(a => a.Identifier).ToHashSet();

        var dates = quotes.Select(q => q.Date).Distinct().ToList();
        var assetIds = imported.Values.Select(a => a.Id).ToList();
        var dayPrices = (await db.Prices.Where(p => dates.Contains(p.Date) && assetIds.Contains(p.AssetId)).ToListAsync(ct))
            .GroupBy(p => (p.AssetId, p.Date))
            .ToDictionary(g => g.Key, g => g.First());

        int createdAssets = 0, renamedAssets = 0, createdPrices = 0, skipped = 0;
        foreach (var (instrument, value, date) in quotes)
        {
            if (!imported.TryGetValue((instrument.Type, instrument.Code), out var asset))
            {
                if (symbols.Contains(instrument.Symbol) || !identifiers.Add(instrument.Code))
                {
                    // Another asset (e.g. a TSETMC fund or a crypto coin) already uses this symbol or identifier.
                    skipped++;
                    logger.LogWarning("Skipping {Code}: symbol {Symbol} or identifier {Code} is already taken", instrument.Code, instrument.Symbol, instrument.Code);
                    continue;
                }

                symbols.Add(instrument.Symbol);
                asset = new Asset { Symbol = instrument.Symbol, Identifier = instrument.Code, Name = instrument.Name, Type = instrument.Type };
                db.Assets.Add(asset);
                imported[(instrument.Type, instrument.Code)] = asset;
                createdAssets++;
            }
            else if (asset.Symbol != instrument.Symbol || asset.Name != instrument.Name)
            {
                renamedAssets += Rename(asset, instrument, symbols) ? 1 : 0;
            }

            // One row per asset per day, updated in place so it ends the day as that day's closing price.
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
            "tgju price sync finished in {Elapsed}: {Instruments} instruments received, {CreatedAssets} assets added, {RenamedAssets} renamed, {CreatedPrices} prices added, {UpdatedPrices} updated, {Stale} stale, {Skipped} skipped (symbol taken)",
            TimeProvider.System.GetElapsedTime(start), quotes.Count, createdAssets, renamedAssets, createdPrices, updatedPrices, stale, skipped);
    }

    // Brings an existing asset's symbol and name in line with TgjuInstruments. The symbol is only changed
    // when no other asset uses it, since symbols are unique. Returns whether anything changed.
    private bool Rename(Asset asset, TgjuInstrument instrument, HashSet<string> symbols)
    {
        var renamed = asset.Name != instrument.Name;
        asset.Name = instrument.Name;

        if (asset.Symbol != instrument.Symbol)
        {
            if (symbols.Add(instrument.Symbol))
            {
                symbols.Remove(asset.Symbol);
                asset.Symbol = instrument.Symbol;
                renamed = true;
            }
            else
            {
                logger.LogWarning("Not changing the symbol of {Code} to {Symbol}: the symbol is already taken", instrument.Code, instrument.Symbol);
            }
        }

        return renamed;
    }
}
