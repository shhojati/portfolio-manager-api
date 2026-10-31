using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Public;

namespace PortfolioManager.Api.Pages;

/// <summary>
/// What the public layout needs from every public page: its title and description for search results, its canonical
/// path, structured data, and the banner's links.
/// </summary>
public abstract class PublicPageModel(IOptions<LandingOptions> landing) : PageModel
{
    public LandingOptions Landing { get; } = landing.Value;

    public string Title { get; protected set; } = PublicSite.Name;
    public string Description { get; protected set; } = "";

    /// <summary>The page's own address, without the query string unless it picks different content (e.g. ?page=2).</summary>
    public string CanonicalPath { get; protected set; } = "/";

    /// <summary>Kept out of search results (search result pages).</summary>
    public bool NoIndex { get; protected set; }

    /// <summary>The category whose chip is highlighted, if any.</summary>
    public string? ActiveCategory { get; protected set; }

    /// <summary>The header link that is highlighted: "prices" or "sources".</summary>
    public string ActiveNav { get; protected set; } = "prices";

    /// <summary>schema.org objects written as JSON-LD in the page head.</summary>
    public List<object> StructuredData { get; } = [];

    public string BaseUrl => PublicSite.BaseUrl(Request, Landing);

    /// <summary>Swagger UI when it's on (it follows "Swagger:Enabled" outside Development), otherwise no API link.</summary>
    public string? ApiDocsUrl
    {
        get
        {
            var services = HttpContext.RequestServices;
            var enabled = services.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
                || services.GetRequiredService<IConfiguration>().GetValue<bool>("Swagger:Enabled");
            return enabled ? "/swagger" : null;
        }
    }

    public string StructuredDataJson(object data) => JsonSerializer.Serialize(data);

    public override void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
        // Matches the output cache, so browsers don't ask again sooner than the page can change.
        Response.Headers.CacheControl = NoIndex ? "no-cache" : $"public, max-age={(int)PublicSite.CacheDuration.TotalSeconds}";
    }

    protected object Breadcrumbs(params (string Name, string Path)[] items) => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = items.Select((item, i) => new Dictionary<string, object>
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["name"] = item.Name,
            ["item"] = BaseUrl + item.Path,
        }).ToList(),
    };
}
