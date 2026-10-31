using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using Chandoo.Api;
using Chandoo.Site;

namespace Chandoo.Pages;

/// <summary>Search results with their prices. Kept out of search engines; cached briefly per query.</summary>
[OutputCache(PolicyName = PublicSite.SearchCachePolicy)]
public sealed class SearchModel(PortfolioApiClient api, IOptions<LandingOptions> landing) : PublicPageModel(landing)
{
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
            await LoadAsync(async () => Results = await api.SearchAsync(Query[..Math.Min(Query.Length, 100)], ct));
    }
}
