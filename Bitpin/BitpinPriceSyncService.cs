using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Bitpin;

/// <summary>
/// Background job that imports every coin with a Toman market on Bitpin as a Crypto <see cref="Asset"/>
/// and keeps one <see cref="Price"/> per asset per day (Tehran calendar day) up to date with the latest price.
/// Prices are stored in Rial, like TSETMC prices.
/// </summary>
public sealed class BitpinPriceSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<BitpinOptions> options,
    ILogger<BitpinPriceSyncService> logger) : BackgroundService
{
    public const string CryptoType = "Crypto";

    private const string TomanSuffix = "_IRT";
    private const int RialsPerToman = 10;

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Bitpin price sync is disabled");
            return;
        }

        logger.LogInformation("Bitpin price sync service started: every {Interval}", opts.Interval);
        using var timer = new PeriodicTimer(opts.Interval);

        try
        {
            do
            {
                var runStart = TimeProvider.System.GetTimestamp();
                try
                {
                    await SyncPricesAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Bitpin being down or changing its API must not kill the job; try again next tick.
                    logger.LogError(ex, "Bitpin sync run failed after {Elapsed}", TimeProvider.System.GetElapsedTime(runStart));
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            logger.LogInformation("Bitpin price sync service stopped");
        }
    }

    private async Task SyncPricesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<BitpinClient>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var start = TimeProvider.System.GetTimestamp();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran));
        var date = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Coin code -> price in Toman, for Toman markets only (the USDT pairs duplicate the same coins).
        var tickers = (await client.GetTickersAsync(ct))
            .Where(t => t.Symbol?.EndsWith(TomanSuffix, StringComparison.Ordinal) == true && t.Price > 0)
            .Select(t => (Code: t.Symbol![..^TomanSuffix.Length].ToUpperInvariant(), Price: t.Price!.Value))
            .Where(t => t.Code.Length is > 0 and <= 20)
            .DistinctBy(t => t.Code)
            .ToList();

        var allAssets = await db.Assets.ToListAsync(ct);
        var cryptos = allAssets.Where(a => a.Type == CryptoType)
            .GroupBy(a => a.Symbol)
            .ToDictionary(g => g.Key, g => g.First());
        var symbols = allAssets.Select(a => a.Symbol).ToHashSet();
        var identifiers = allAssets.Select(a => a.Identifier).ToHashSet();
        var dayPrices = (await db.Prices.Where(p => p.Date == date).ToListAsync(ct))
            .GroupBy(p => p.AssetId)
            .ToDictionary(g => g.Key, g => g.First());

        // Names only come from the (large) markets list, so fetch it just when there is a coin we don't have yet.
        Dictionary<string, string> names = [];
        if (tickers.Any(t => !cryptos.ContainsKey(t.Code)))
        {
            names = (await client.GetMarketsAsync(ct))
                .Where(m => m.Base?.Code is not null)
                .GroupBy(m => m.Base!.Code!.ToUpperInvariant())
                .ToDictionary(g => g.Key, g => Name(g.First().Base!));
        }

        int createdAssets = 0, createdPrices = 0, skipped = 0;
        foreach (var (code, toman) in tickers)
        {
            if (!cryptos.TryGetValue(code, out var asset))
            {
                if (!symbols.Add(code) || !identifiers.Add(code))
                {
                    // A non-crypto asset (e.g. one added by hand) already uses this symbol or identifier.
                    skipped++;
                    logger.LogDebug("Skipping {Code}: symbol or identifier is already taken", code);
                    continue;
                }

                asset = new Asset { Symbol = code, Identifier = code, Name = names.GetValueOrDefault(code, code), Type = CryptoType };
                db.Assets.Add(asset);
                cryptos[code] = asset;
                createdAssets++;
            }

            // One row per asset per day, updated in place so it ends the day as that day's closing price.
            var value = toman * RialsPerToman;
            if (asset.Id != 0 && dayPrices.TryGetValue(asset.Id, out var price))
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
            "Bitpin price sync finished in {Elapsed} for {Date:yyyy-MM-dd}: {Coins} coins received, {CreatedAssets} assets added, {CreatedPrices} prices added, {UpdatedPrices} updated, {Skipped} skipped (symbol taken)",
            TimeProvider.System.GetElapsedTime(start), today, tickers.Count, createdAssets, createdPrices, updatedPrices, skipped);
    }

    // Persian name like TSETMC assets (e.g. "بیت کوین"), falling back to the English one. Arabic ي/ك become Persian ی/ک.
    private static string Name(Currency currency)
    {
        var name = (string.IsNullOrWhiteSpace(currency.TitleFa) ? currency.Title ?? currency.Code! : currency.TitleFa)
            .Replace('ي', 'ی').Replace('ك', 'ک').Trim();
        return name.Length > 100 ? name[..100] : name;
    }
}
