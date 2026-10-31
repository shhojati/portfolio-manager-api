using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Tgju;
using PortfolioManager.Api.Tsetmc;

namespace PortfolioManager.Api.Public;

/// <summary>An asset's latest price on a public page, with its daily change and recent trend.</summary>
/// <param name="Detail">The second line under the symbol: the full name, or the code when the name is the symbol.</param>
/// <param name="ChangePercent">Change from the asset's previous price; null when it has only one.</param>
/// <param name="Trend">The last <see cref="PublicPriceQuery.TrendPoints"/> values, oldest first.</param>
public sealed record PriceRow(
    string Symbol,
    string Detail,
    string Type,
    string? Unit,
    decimal Value,
    DateTime Date,
    decimal? ChangePercent,
    IReadOnlyList<decimal> Trend);

public sealed record PricePage(PriceCategory Category, IReadOnlyList<PriceRow> Rows, int Page, int PageCount, int Total)
{
    public DateTime? LastUpdated => Rows.Count == 0 ? null : Rows.Max(r => r.Date);
}

/// <summary>Reads the latest prices for the public pages straight from the database (no API call, no rate limit).</summary>
public sealed class PublicPriceQuery(AppDbContext db)
{
    public const int TrendPoints = 30;

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    private static readonly StringComparer PersianOrder = StringComparer.Create(CultureInfo.GetCultureInfo("fa-IR"), ignoreCase: false);

    // tgju's instruments are listed in the order they're written in (dollar, euro...; 18 karat gold first), not in the
    // order they happened to be added.
    private static readonly Dictionary<string, int> TgjuOrder = TgjuInstruments.ByKey.Values
        .Select((instrument, i) => (instrument.Code, i))
        .ToDictionary(x => x.Code, x => x.i);

    // A Latin code worth showing under the symbol (USD, BTC), unlike internal ones such as COIN-EMAMI or an ISIN.
    private static readonly Regex ShortCode = new("^[A-Z]{2,6}$", RegexOptions.Compiled);

    private sealed record AssetInfo(int Id, string Symbol, string Name, string Identifier, string Type, string? Unit);

    private sealed class PricePoint
    {
        public int AssetId { get; set; }
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
    }

    /// <summary>One page of a category's assets that have a price, in the category's order.</summary>
    public async Task<PricePage> GetCategoryAsync(PriceCategory category, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Assets.AsNoTracking().Where(a => category.Types.Contains(a.Type));
        if (category.ActiveDays is { } days)
        {
            var since = TehranToday().AddDays(-days);
            query = query.Where(a => a.Prices.Any(p => p.Date >= since));
        }
        else
            query = query.Where(a => a.Prices.Any());

        var assets = await query
            .Select(a => new AssetInfo(a.Id, a.Symbol, a.Name, a.Identifier, a.Type, a.Unit))
            .ToListAsync(ct);
        assets = category.ByName
            ? assets.OrderBy(a => a.Symbol, PersianOrder).ToList()
            : assets.OrderBy(a => TgjuOrder.GetValueOrDefault(a.Identifier, int.MaxValue)).ThenBy(a => a.Id).ToList();

        var pageCount = Math.Max(1, (assets.Count + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, pageCount);
        var rows = await RowsAsync(assets.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ct);
        return new PricePage(category, rows, page, pageCount, assets.Count);
    }

    /// <summary>The assets with these identifiers, in the order given; ones that don't exist or have no price are left out.</summary>
    public async Task<IReadOnlyList<PriceRow>> GetByIdentifiersAsync(IReadOnlyList<string> identifiers, CancellationToken ct)
    {
        var assets = await db.Assets.AsNoTracking()
            .Where(a => identifiers.Contains(a.Identifier))
            .Select(a => new AssetInfo(a.Id, a.Symbol, a.Name, a.Identifier, a.Type, a.Unit))
            .ToListAsync(ct);
        return await RowsAsync(assets.OrderBy(a => identifiers.ToList().IndexOf(a.Identifier)).ToList(), ct);
    }

    /// <summary>The best matches for a search, as on GET /api/assets/search, with their prices.</summary>
    public async Task<IReadOnlyList<PriceRow>> SearchAsync(string q, int limit, CancellationToken ct)
    {
        var query = AssetSearch.Apply(db.Assets.AsNoTracking().Where(a => a.Prices.Any()), q);
        if (query is null)
            return [];
        var assets = await query
            .Select(a => new AssetInfo(a.Id, a.Symbol, a.Name, a.Identifier, a.Type, a.Unit))
            .Take(limit)
            .ToListAsync(ct);
        return await RowsAsync(assets, ct);
    }

    // The latest TrendPoints prices of each asset in one query; the change is the latest against the one before it.
    private async Task<IReadOnlyList<PriceRow>> RowsAsync(List<AssetInfo> assets, CancellationToken ct)
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

        var rows = new List<PriceRow>(assets.Count);
        foreach (var asset in assets)
        {
            var trend = byAsset[asset.Id].OrderBy(p => p.Date).ToList();
            if (trend.Count == 0)
                continue;
            var last = trend[^1];
            decimal? change = trend.Count > 1 && trend[^2].Value != 0
                ? Math.Round((last.Value - trend[^2].Value) / trend[^2].Value * 100, 2)
                : null;
            var detail = asset.Name != asset.Symbol ? asset.Name
                : asset.Identifier != asset.Symbol && ShortCode.IsMatch(asset.Identifier) ? asset.Identifier
                : "";
            // «هر واحد» says nothing for an index.
            var unit = asset.Unit == TsetmcIndices.Unit ? null : asset.Unit;
            rows.Add(new PriceRow(asset.Symbol, detail, asset.Type, unit, last.Value, last.Date, change,
                trend.Select(p => p.Value).ToList()));
        }
        return rows;
    }

    // Prices are stored per Tehran day, at midnight UTC.
    private static DateTime TehranToday() =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran).Date, DateTimeKind.Utc);
}
