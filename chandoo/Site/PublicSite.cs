using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Chandoo.Site;

/// <summary>robots.txt, sitemap.xml and the site's base address for canonical links.</summary>
public static class PublicSite
{
    public const string Name = "چندو";
    public const string LatinName = "CHANDOO";

    /// <summary>The output cache policy for the public pages: prices change every 15 minutes at most.</summary>
    public const string CachePolicy = "PublicPages";

    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>The output cache policy for search results: one entry per query, kept briefly.</summary>
    public const string SearchCachePolicy = "Search";

    /// <summary>The site's address without a trailing slash: "Landing:SiteUrl", or the one the request came in on.</summary>
    public static string BaseUrl(HttpRequest request, LandingOptions options) =>
        !string.IsNullOrWhiteSpace(options.SiteUrl)
            ? options.SiteUrl.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}{request.PathBase}";

    public static void MapPublicSite(this WebApplication app)
    {
        app.MapGet("/robots.txt", (HttpRequest request, IOptions<LandingOptions> options) =>
            Results.Text(new StringBuilder()
                .AppendLine("User-agent: *")
                .AppendLine("Disallow: /search")
                .AppendLine($"Sitemap: {BaseUrl(request, options.Value)}/sitemap.xml")
                .ToString(), "text/plain; charset=utf-8"))
            .CacheOutput(CachePolicy);

        app.MapGet("/sitemap.xml", (HttpRequest request, IOptions<LandingOptions> options) =>
        {
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var baseUrl = BaseUrl(request, options.Value);
            var paths = new[] { "/", "/sources" }.Concat(PriceCategory.All.Select(c => c.Path));
            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(ns + "urlset",
                paths.Select(p => new XElement(ns + "url", new XElement(ns + "loc", baseUrl + p)))));
            return Results.Text(doc.Declaration + doc.ToString(SaveOptions.DisableFormatting), "application/xml; charset=utf-8");
        }).CacheOutput(CachePolicy);
    }
}
