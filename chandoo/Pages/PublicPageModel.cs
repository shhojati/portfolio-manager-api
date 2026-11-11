using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Chandoo.Site;

namespace Chandoo.Pages;

/// <summary>
/// What the layout needs from every page: its title and description for search results, its canonical path, structured
/// data, and the banner's links.
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

    /// <summary>The header link that is highlighted: "prices" or "sources".</summary>
    public string ActiveNav { get; protected set; } = "prices";

    /// <summary>The prices couldn't be read from the API; the layout displays an availability notice.</summary>
    public bool Unavailable { get; private set; }

    /// <summary>schema.org objects written as JSON-LD in the page head.</summary>
    public List<object> StructuredData { get; } = [];

    public string BaseUrl => PublicSite.BaseUrl(Request, Landing);

    public string StructuredDataJson(object data) => JsonSerializer.Serialize(data);

    public override void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
        // Matches the output cache, so browsers don't ask again sooner than the page can change.
        Response.Headers.CacheControl = NoIndex || Unavailable
            ? "no-cache"
            : $"public, max-age={(int)PublicSite.CacheDuration.TotalSeconds}";
    }

    /// <summary>
    /// Runs <paramref name="load"/>, the page's calls to the API. When the API can't be reached or answers with an error,
    /// the page is a 503 that tells the visitor to try again (and isn't cached), rather than an error page.
    /// </summary>
    protected async Task LoadAsync(Func<Task> load)
    {
        try
        {
            await load();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<PublicPageModel>>()
                .LogWarning(e, "Couldn't read prices from the API for {Path}", Request.Path);
            Unavailable = true;
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
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
