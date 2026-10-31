using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.Tgju;

namespace PortfolioManager.Api.Board;

/// <summary>
/// Latest prices with their daily change and recent trend, a list at a time, for price boards (see BoardController).
/// </summary>
public sealed class PriceBoard(AppDbContext db)
{
    public const int TrendPoints = 30;

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    private static readonly StringComparer PersianOrder = StringComparer.Create(CultureInfo.GetCultureInfo("fa-IR"), ignoreCase: false);

    // tgju's instruments are listed in the order they're written in (dollar, euro...; 18 karat gold first), not in the
    // order they happened to be added.
    private static readonly Dictionary<string, int> TgjuOrder = TgjuInstruments.ByKey.Values
        .Select((instrument, i) => (instrument.Code, i))
        .ToDictionary(x => x.Code, x => x.i);

    private sealed record AssetInfo(int Id, string Identifier, string Symbol, string Name, string Type, string? Unit);

    private sealed class PricePoint
    {
        public int AssetId { get; set; }
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
    }

    /// <summary>
    /// One page of the assets of these types that have a price, by symbol (Persian order) when <paramref name="byName"/>,
    /// otherwise tgju's instruments in their own order and the rest in the order they were added.
    /// </summary>
    /// <param name="activeDays">Only assets with a price this recent, so delisted stocks drop off; null for all.</param>
    public async Task<BoardPageDto> GetPageAsync(
        IReadOnlyList<string> types, bool byName, int? activeDays, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Assets.AsNoTracking().Where(a => types.Contains(a.Type));
        if (activeDays is { } days)
        {
            var since = TehranToday().AddDays(-days);
            query = query.Where(a => a.Prices.Any(p => p.Date >= since));
        }
        else
            query = query.Where(a => a.Prices.Any());

        var assets = await query
            .Select(a => new AssetInfo(a.Id, a.Identifier, a.Symbol, a.Name, a.Type, a.Unit))
            .ToListAsync(ct);
        assets = byName
            ? assets.OrderBy(a => a.Symbol, PersianOrder).ToList()
            : assets.OrderBy(a => TgjuOrder.GetValueOrDefault(a.Identifier, int.MaxValue)).ThenBy(a => a.Id).ToList();

        var pageCount = Math.Max(1, (assets.Count + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, pageCount);
        var items = await ItemsAsync(assets.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ct);
        return new BoardPageDto(assets.Count, page, pageCount, items);
    }

    /// <summary>The assets with these identifiers, in the order given; ones that don't exist or have no price are left out.</summary>
    public async Task<IReadOnlyList<BoardItemDto>> GetByIdentifiersAsync(IReadOnlyList<string> identifiers, CancellationToken ct)
    {
        var assets = await db.Assets.AsNoTracking()
            .Where(a => identifiers.Contains(a.Identifier))
            .Select(a => new AssetInfo(a.Id, a.Identifier, a.Symbol, a.Name, a.Type, a.Unit))
            .ToListAsync(ct);
        var order = identifiers.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.OrdinalIgnoreCase);
        return await ItemsAsync(assets.OrderBy(a => order.GetValueOrDefault(a.Identifier)).ToList(), ct);
    }

    /// <summary>The best matches for a search, as on GET /api/assets/search, that have a price.</summary>
    public async Task<IReadOnlyList<BoardItemDto>> SearchAsync(string q, int limit, CancellationToken ct)
    {
        var query = AssetSearch.Apply(db.Assets.AsNoTracking().Where(a => a.Prices.Any()), q);
        if (query is null)
            return [];
        var assets = await query
            .Select(a => new AssetInfo(a.Id, a.Identifier, a.Symbol, a.Name, a.Type, a.Unit))
            .Take(limit)
            .ToListAsync(ct);
        return await ItemsAsync(assets, ct);
    }

    // The latest TrendPoints prices of each asset in one query; the change is the latest against the one before it.
    private async Task<IReadOnlyList<BoardItemDto>> ItemsAsync(List<AssetInfo> assets, CancellationToken ct)
    {
        if (assets.Count == 0)
            return [];

        var ids = JsonSerializer.Serialize(assets.Select(a => a.Id));
        var points = await db.Database.SqlQuery<PricePoint>($"""
            SELECT "AssetId", "Date", "Value" FROM (
                SELECT "AssetId", "Date", "Value",
                       ROW_NUMBER() OVER (PARTITION BY "AssetId" ORDER BY "Date" DESC) AS "Rank"
                FROM "Prices"
                WHERE "AssetId" IN (SELECT value FROM json_each({ids})))
            WHERE "Rank" <= {TrendPoints}
            """).ToListAsync(ct);
        var byAsset = points.ToLookup(p => p.AssetId);

        var items = new List<BoardItemDto>(assets.Count);
        foreach (var asset in assets)
        {
            var trend = byAsset[asset.Id].OrderBy(p => p.Date).ToList();
            if (trend.Count == 0)
                continue;
            var last = trend[^1];
            decimal? change = trend.Count > 1 && trend[^2].Value != 0
                ? Math.Round((last.Value - trend[^2].Value) / trend[^2].Value * 100, 2)
                : null;
            items.Add(new BoardItemDto(asset.Identifier, asset.Symbol, asset.Name, asset.Type, asset.Unit,
                last.Value, DateTime.SpecifyKind(last.Date, DateTimeKind.Utc), change, trend.Select(p => p.Value).ToList()));
        }
        return items;
    }

    // Prices are stored per Tehran day, at midnight UTC.
    private static DateTime TehranToday() =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran).Date, DateTimeKind.Utc);
}
