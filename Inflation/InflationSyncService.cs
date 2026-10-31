using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Inflation;

/// <summary>
/// Imports Iran's consumer price index (see <see cref="CpiTable"/>) as an <see cref="Asset"/> with one
/// <see cref="Price"/> per month, so the app can chart inflation next to a portfolio's return like any other asset. The
/// table is kept by hand, so this runs once at startup: a month added to it reaches the database with the next deploy.
/// A month's index is dated the month's last day; the history endpoint carries it forward to the days after it.
/// </summary>
public sealed class InflationSyncService(IServiceScopeFactory scopeFactory, ILogger<InflationSyncService> logger) : BackgroundService
{
    public const string IndexType = "Index";
    public const string Identifier = "CPI-IR";
    public const string Symbol = "تورم";
    public const string Name = "شاخص قیمت مصرف‌کننده (تورم)";
    public const string Unit = "واحد";

    private static readonly PersianCalendar Persian = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BroadcastChanges = false;

            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Identifier == Identifier, stoppingToken);
            if (asset is null)
            {
                if (await db.Assets.AnyAsync(a => a.Symbol == Symbol, stoppingToken))
                {
                    logger.LogWarning("Inflation sync: symbol '{Symbol}' is already taken", Symbol);
                    return;
                }

                asset = new Asset { Symbol = Symbol, Identifier = Identifier, Name = Name, Type = IndexType, Unit = Unit };
                db.Assets.Add(asset);
            }

            var stored = asset.Id == 0
                ? []
                : await db.Prices.Where(p => p.AssetId == asset.Id).ToDictionaryAsync(p => p.Date, stoppingToken);
            int added = 0, updated = 0;
            foreach (var (year, month, value) in CpiTable.Months)
            {
                var date = DateTime.SpecifyKind(Persian.ToDateTime(year, month, Persian.GetDaysInMonth(year, month), 0, 0, 0, 0), DateTimeKind.Utc);
                if (stored.TryGetValue(date, out var price))
                {
                    if (price.Value != value)
                    {
                        price.Value = value;
                        updated++;
                    }
                }
                else
                {
                    db.Prices.Add(new Price { Asset = asset, Value = value, Date = date });
                    added++;
                }
            }

            await db.SaveChangesAsync(stoppingToken);
            logger.LogInformation("Inflation sync: {Months} months in the table, {Added} added, {Updated} updated (base {Base})",
                CpiTable.Months.Count, added, updated, CpiTable.Base);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Inflation sync failed; it runs again at the next startup");
        }
    }
}
