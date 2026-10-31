using System.Text.Encodings.Web;
using System.Text.Unicode;
using Chandoo.Api;
using Chandoo.Site;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

// Server-rendered pages, so search engines can index the prices without running any JavaScript.
builder.Services.AddRazorPages();
// Write Persian text as is rather than as &#x...; entities, which would triple the size of the pages.
builder.Services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.Configure<LandingOptions>(builder.Configuration.GetSection(LandingOptions.SectionName));

// Prices come from the Portfolio Manager API, signed in as an API client (see Api/ApiTokenProvider.cs).
builder.Services.AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Api:BaseUrl must be the API's address, e.g. https://portfolio-api.shahabdev.ir.")
    .Validate(o => o.Username != "" && o.Password != "", "Api:Username and Api:Password must be an API client account on the API.")
    .ValidateOnStart();
void ConfigureApi(IServiceProvider sp, HttpClient http)
{
    var options = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = options.Timeout;
}
builder.Services.AddSingleton<ApiTokenProvider>();
builder.Services.AddTransient<ApiTokenHandler>();
builder.Services.AddHttpClient(ApiTokenProvider.ClientName, ConfigureApi);
builder.Services.AddHttpClient<PortfolioApiClient>(ConfigureApi).AddHttpMessageHandler<ApiTokenHandler>();

// Pages are kept for a few minutes, so visitors and crawlers don't each cost a call to the API.
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy(PublicSite.CachePolicy, policy => policy.Expire(PublicSite.CacheDuration).SetVaryByQuery("page"));
    options.AddPolicy(PublicSite.SearchCachePolicy, policy => policy.Expire(TimeSpan.FromMinutes(2)).SetVaryByQuery("q"));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

// The font never changes under the same name; the stylesheet may change with a deploy.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
        ctx.File.Name.EndsWith(".woff2", StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "public, max-age=3600"
});

app.UseOutputCache();
app.MapRazorPages();
app.MapPublicSite();
app.MapGet("/error", () => Results.Text("خطایی رخ داد. چند دقیقهٔ دیگر دوباره سر بزنید.", "text/plain; charset=utf-8", statusCode: 500));

app.Run();
