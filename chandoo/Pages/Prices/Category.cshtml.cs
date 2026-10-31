using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using Chandoo.Api;
using Chandoo.Site;

namespace Chandoo.Pages.Prices;

/// <summary>Every asset of one category with its latest price, a page at a time.</summary>
[OutputCache(PolicyName = PublicSite.CachePolicy)]
public sealed class CategoryModel(PortfolioApiClient api, IOptions<LandingOptions> landing) : PublicPageModel(landing)
{
    private const int PageSize = 50;

    public PriceCategory Category { get; private set; } = null!;
    public PricePage Prices { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (PriceCategory.Find(slug) is not { } category)
            return NotFound();
        // One address per page: /prices/Currency and ?page=1 go to the plain lowercase one.
        if (slug != category.Slug || Request.Query.ContainsKey("page") && page <= 1)
            return RedirectPermanent(category.Path);

        Category = category;
        Prices = new PricePage(category, [], 1, 1, 0);
        await LoadAsync(async () => Prices = await api.GetCategoryAsync(category, page, PageSize, ct));
        if (!Unavailable && Prices.Page != page && page > 1)
            return RedirectPermanent(Prices.Page > 1 ? $"{category.Path}?page={Prices.Page}" : category.Path);

        var pageSuffix = Prices.Page > 1 ? $" — صفحهٔ {PersianFormat.Digits(Prices.Page)}" : "";
        Title = $"{category.Heading} امروز{pageSuffix} | {PublicSite.Name}";
        Description = category.Description;
        CanonicalPath = Prices.Page > 1 ? $"{category.Path}?page={Prices.Page}" : category.Path;
        StructuredData.Add(Breadcrumbs((PublicSite.Name, "/"), (category.Heading, category.Path)));
        return Page();
    }
}
