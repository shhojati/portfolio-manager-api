using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Public;

namespace PortfolioManager.Api.Pages;

/// <summary>Search results with their prices. Kept out of search engines and not cached: every query is different.</summary>
public sealed class SearchModel(PublicPriceQuery prices, IOptions<LandingOptions> landing) : PublicPageModel(landing)
{
    private const int MaxResults = 50;

    public string? Query { get; private set; }
    public IReadOnlyList<PriceRow> Results { get; private set; } = [];

    public async Task OnGetAsync([FromQuery] string? q, CancellationToken ct)
    {
        Query = q?.Trim();
        Title = string.IsNullOrEmpty(Query) ? $"جست‌وجو | {PublicSite.Name}" : $"جست‌وجوی «{Query}» | {PublicSite.Name}";
        Description = "جست‌وجوی قیمت سهام، صندوق، ارز، طلا و رمزارز.";
        CanonicalPath = "/search";
        NoIndex = true;
        if (!string.IsNullOrEmpty(Query))
            Results = await prices.SearchAsync(Query[..Math.Min(Query.Length, 100)], MaxResults, ct);
    }
}
