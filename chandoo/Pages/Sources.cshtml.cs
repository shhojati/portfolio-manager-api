using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using Chandoo.Site;

namespace Chandoo.Pages;

/// <summary>Where each category's prices come from and how often the API fetches them.</summary>
[OutputCache(PolicyName = PublicSite.CachePolicy)]
public sealed class SourcesModel(IOptions<LandingOptions> landing) : PublicPageModel(landing)
{
    public sealed record Source(string Name, string Url, string Data, string Schedule);

    // Matches the API's sync jobs as configured in its appsettings.json (Tsetmc, TsetmcDerivatives, Tgju, Bitpin,
    // Fipiran, Ime, ImeDerivatives) and Inflation/CpiTable.cs; update this list when a schedule there changes.
    public static readonly IReadOnlyList<Source> Sources =
    [
        new("TSETMC (مدیریت فناوری بورس تهران)", "https://www.tsetmc.com",
            "سهام، صندوق‌های قابل معامله، گواهی‌های سپرده، اوراق سلف و شاخص کل", "ساعتی، شنبه تا چهارشنبه از ۸:۴۵ تا ۱۳:۰۰"),
        new("TSETMC (مدیریت فناوری بورس تهران)", "https://www.tsetmc.com",
            "اختیار معامله و قرارداد آتی سهام", "هر ۵ دقیقه، شنبه تا چهارشنبه از ۸:۴۵ تا ۱۳:۰۰"),
        new("tgju", "https://www.tgju.org", "ارز، طلا، سکه و فلزات", "هر ۱۵ دقیقه"),
        new("بیت‌پین", "https://bitpin.ir", "رمزارزها (بازارهای تومانی)", "هر ۱۵ دقیقه"),
        new("فیپیران", "https://www.fipiran.ir", "NAV صندوق‌های صدور و ابطال", "ساعتی"),
        new("بورس کالای ایران", "https://www.ime.co.ir", "گواهی‌های سپردهٔ پیوستهٔ کالایی", "ساعتی"),
        new("بورس کالای ایران", "https://www.ime.co.ir", "اختیار معامله بورس کالا", "ساعتی؛ گزارش هر روز عصرِ همان روز منتشر می‌شود"),
        new("مرکز آمار ایران", "https://www.amar.org.ir", "شاخص قیمت مصرف‌کننده (تورم)", "ماهانه، پس از انتشار گزارش مرکز آمار"),
    ];

    /// <summary>Each source once, by its short name (without the part in brackets), for the footer.</summary>
    public static readonly IReadOnlyList<(string Name, string Url)> FooterSources = Sources
        .DistinctBy(s => s.Url)
        .Select(s => (s.Name.Split(" (")[0], s.Url))
        .ToList();

    public void OnGet()
    {
        Title = $"منابع قیمت‌ها | {PublicSite.Name}";
        Description = "قیمت‌های چندو از کجا می‌آیند و هر چند وقت یک بار به‌روز می‌شوند: TSETMC، tgju، بیت‌پین، فیپیران، بورس کالا و مرکز آمار ایران.";
        CanonicalPath = "/sources";
        ActiveNav = "sources";
        StructuredData.Add(Breadcrumbs((PublicSite.Name, "/"), ("منابع قیمت‌ها", "/sources")));
    }
}
