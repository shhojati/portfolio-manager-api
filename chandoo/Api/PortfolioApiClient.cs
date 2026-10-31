using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Chandoo.Site;

namespace Chandoo.Api;

/// <summary>An asset's latest price on a page, with its daily change and recent trend.</summary>
/// <param name="Detail">The second line under the symbol: the full name, or a short code (USD, BTC) when the name is the symbol.</param>
/// <param name="Unit">What one price is for («گرم», «عدد»...); null when there's nothing worth saying.</param>
/// <param name="ChangePercent">Change from the asset's previous price; null when it has only one.</param>
/// <param name="Trend">The last few values, oldest first.</param>
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

/// <summary>Reads prices from the Portfolio Manager API's price board (GET /api/board).</summary>
public sealed partial class PortfolioApiClient(HttpClient http)
{
    // The API's BoardItemDto and BoardPageDto.
    private sealed record BoardItem(
        string Identifier, string Symbol, string Name, string Type, string? Unit,
        decimal Value, DateTime Date, decimal? ChangePercent, List<decimal> Trend);

    private sealed record BoardPage(int Total, int Page, int PageCount, List<BoardItem> Items);

    // A Latin code worth showing under the symbol (USD, BTC), unlike internal ones such as COIN-EMAMI or an ISIN.
    [GeneratedRegex("^[A-Z]{2,6}$")]
    private static partial Regex ShortCode();

    /// <summary>One page of a category's assets that have a price, in the category's order.</summary>
    public async Task<PricePage> GetCategoryAsync(PriceCategory category, int page, int pageSize, CancellationToken ct)
    {
        var query = string.Join("&", category.Types.Select(t => $"types={Uri.EscapeDataString(t)}"))
            + $"&order={(category.ByName ? "name" : "listed")}&activeDays={category.ActiveDays ?? 0}&page={page}&pageSize={pageSize}";
        var result = await http.GetFromJsonAsync<BoardPage>($"api/board?{query}", ct)
            ?? throw new HttpRequestException("The API's answer was empty.");
        return new PricePage(category, result.Items.Select(ToRow).ToList(), result.Page, result.PageCount, result.Total);
    }

    /// <summary>The assets with these identifiers, in the order given; ones that don't exist or have no price are left out.</summary>
    public async Task<IReadOnlyList<PriceRow>> GetByIdentifiersAsync(IEnumerable<string> identifiers, CancellationToken ct)
    {
        var query = string.Join("&", identifiers.Select(i => $"identifiers={Uri.EscapeDataString(i)}"));
        var items = await http.GetFromJsonAsync<List<BoardItem>>($"api/board/by-identifiers?{query}", ct) ?? [];
        return items.Select(ToRow).ToList();
    }

    /// <summary>The best matches for a search, with their prices.</summary>
    public async Task<IReadOnlyList<PriceRow>> SearchAsync(string q, CancellationToken ct)
    {
        var items = await http.GetFromJsonAsync<List<BoardItem>>($"api/board/search?q={Uri.EscapeDataString(q)}", ct) ?? [];
        return items.Select(ToRow).ToList();
    }

    private static PriceRow ToRow(BoardItem item)
    {
        var detail = item.Name != item.Symbol ? item.Name
            : item.Identifier != item.Symbol && ShortCode().IsMatch(item.Identifier) ? item.Identifier
            : "";
        var unit = item.Unit == AssetTypes.IndexUnit ? null : item.Unit;
        return new PriceRow(item.Symbol, detail, item.Type, unit, item.Value, item.Date, item.ChangePercent, item.Trend);
    }
}
