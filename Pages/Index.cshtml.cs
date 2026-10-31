using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Public;
using PortfolioManager.Api.Tsetmc;

namespace PortfolioManager.Api.Pages;

/// <summary>The site's home page: headline prices, a short table per main category, and a FAQ.</summary>
[OutputCache(PolicyName = PublicSite.CachePolicy)]
public sealed class IndexModel(PublicPriceQuery prices, IOptions<LandingOptions> landing) : PublicPageModel(landing)
{
    private const int RowsPerSection = 6;

    // The headline cards: the free-market dollar, 18 karat gold and the TSE total index.
    private static readonly string[] HeadlineIdentifiers = ["USD", "GOLD-18", TsetmcIndices.TotalIdentifier];

    private static readonly string[] SectionSlugs = ["currency", "gold-coin", "crypto", "index"];

    public static readonly IReadOnlyList<(string Question, string Answer)> Faq =
    [
        ("قیمت‌ها از کجا می‌آیند؟",
            "قیمت سهام، صندوق‌های بورسی و شاخص کل از سایت مدیریت فناوری بورس تهران (TSETMC)، ارز و طلا و سکه از tgju، رمزارزها از بیت‌پین، صندوق‌های صدور و ابطال از فیپیران و گواهی‌های کالایی از بورس کالا گرفته می‌شود. فهرست کامل در صفحهٔ منابع آمده است."),
        ("قیمت‌ها هر چند وقت یک بار به‌روز می‌شوند؟",
            "ارز، طلا و رمزارز در طول روز چند بار به‌روز می‌شوند و قیمت‌های بورس در ساعت بازار. زمان به‌روزرسانی هر منبع در صفحهٔ منابع آمده است."),
        ("قیمت‌ها به چه واحدی است؟",
            "همهٔ قیمت‌ها به ریال است. شاخص کل بورس و شاخص قیمت مصرف‌کننده عدد شاخص‌اند، نه قیمت."),
        ("چطور از این قیمت‌ها در نرم‌افزار خودم استفاده کنم؟",
            "همین قیمت‌ها و تاریخچه‌شان از راه API چندو در دسترس است؛ راهنمای آن در مستندات API آمده است."),
    ];

    public IReadOnlyList<PriceRow> Headlines { get; private set; } = [];
    public List<PricePage> Sections { get; } = [];
    public DateTime? LastUpdated { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Title = $"قیمت روز ارز، طلا، سکه، رمزارز و بورس | {PublicSite.Name}";
        Description = "قیمت روز دلار، یورو، طلای ۱۸ عیار، سکه امامی، بیت‌کوین، سهام بورس تهران و صندوق‌ها به ریال، با تغییر روزانه و روند اخیر.";

        Headlines = await prices.GetByIdentifiersAsync(HeadlineIdentifiers, ct);
        foreach (var slug in SectionSlugs)
            Sections.Add(await prices.GetCategoryAsync(PriceCategory.Find(slug)!, 1, RowsPerSection, ct));
        LastUpdated = Sections.Select(s => s.LastUpdated).Max();

        StructuredData.Add(new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "WebSite",
            ["name"] = PublicSite.Name,
            ["alternateName"] = PublicSite.LatinName,
            ["url"] = BaseUrl + "/",
            ["inLanguage"] = "fa-IR",
            ["potentialAction"] = new Dictionary<string, object>
            {
                ["@type"] = "SearchAction",
                ["target"] = BaseUrl + "/search?q={q}",
                ["query-input"] = "required name=q",
            },
        });
        StructuredData.Add(new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "FAQPage",
            ["mainEntity"] = Faq.Select(f => new Dictionary<string, object>
            {
                ["@type"] = "Question",
                ["name"] = f.Question,
                ["acceptedAnswer"] = new Dictionary<string, object> { ["@type"] = "Answer", ["text"] = f.Answer },
            }).ToList(),
        });
    }
}
