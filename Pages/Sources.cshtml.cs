using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Bitpin;
using PortfolioManager.Api.Fipiran;
using PortfolioManager.Api.Ime;
using PortfolioManager.Api.Public;
using PortfolioManager.Api.Tgju;
using PortfolioManager.Api.Tsetmc;

namespace PortfolioManager.Api.Pages;

/// <summary>
/// Where each category's prices come from and how often they are fetched, read from the sync jobs' own settings so the
/// page stays true when a schedule changes. Jobs that are turned off aren't listed.
/// </summary>
[OutputCache(PolicyName = PublicSite.CachePolicy)]
public sealed class SourcesModel(
    IOptions<LandingOptions> landing,
    IOptions<TsetmcOptions> tsetmc,
    IOptions<TsetmcDerivativesOptions> tsetmcDerivatives,
    IOptions<TgjuOptions> tgju,
    IOptions<BitpinOptions> bitpin,
    IOptions<FipiranOptions> fipiran,
    IOptions<ImeOptions> ime,
    IOptions<ImeDerivativesOptions> imeDerivatives) : PublicPageModel(landing)
{
    public sealed record Source(string Name, string Url, string Data, string Schedule);

    public List<Source> Sources { get; } = [];

    public void OnGet()
    {
        Title = $"منابع قیمت‌ها | {PublicSite.Name}";
        Description = "قیمت‌های چندو از کجا می‌آیند و هر چند وقت یک بار به‌روز می‌شوند: TSETMC، tgju، بیت‌پین، فیپیران، بورس کالا و مرکز آمار ایران.";
        CanonicalPath = "/sources";
        ActiveNav = "sources";
        StructuredData.Add(Breadcrumbs((PublicSite.Name, "/"), ("منابع قیمت‌ها", "/sources")));

        static string InMarketHours(TimeSpan interval, DayOfWeek[] days, TimeOnly open, TimeOnly close) =>
            $"{PersianFormat.Interval(interval)}، {PersianFormat.WeekDays(days)} از {PersianFormat.Time(open)} تا {PersianFormat.Time(close)}";

        var t = tsetmc.Value;
        if (t.Enabled)
            Sources.Add(new("TSETMC (مدیریت فناوری بورس تهران)", "https://www.tsetmc.com",
                "سهام، صندوق‌های قابل معامله، گواهی‌های سپرده، اوراق سلف و شاخص کل",
                InMarketHours(t.Interval, t.TradingDays, t.MarketOpen, t.MarketClose)));
        var td = tsetmcDerivatives.Value;
        if (td.Enabled)
            Sources.Add(new("TSETMC (مدیریت فناوری بورس تهران)", "https://www.tsetmc.com",
                "اختیار معامله و قرارداد آتی سهام",
                InMarketHours(td.Interval, td.TradingDays, td.MarketOpen, td.MarketClose)));
        if (tgju.Value.Enabled)
            Sources.Add(new("tgju", "https://www.tgju.org", "ارز، طلا، سکه و فلزات", PersianFormat.Interval(tgju.Value.Interval)));
        if (bitpin.Value.Enabled)
            Sources.Add(new("بیت‌پین", "https://bitpin.ir", "رمزارزها (بازارهای تومانی)", PersianFormat.Interval(bitpin.Value.Interval)));
        if (fipiran.Value.Enabled)
            Sources.Add(new("فیپیران", "https://www.fipiran.ir", "NAV صندوق‌های صدور و ابطال", PersianFormat.Interval(fipiran.Value.Interval)));
        if (ime.Value.Enabled)
            Sources.Add(new("بورس کالای ایران", "https://www.ime.co.ir", "گواهی‌های سپردهٔ پیوستهٔ کالایی", PersianFormat.Interval(ime.Value.Interval)));
        if (imeDerivatives.Value.Enabled)
            Sources.Add(new("بورس کالای ایران", "https://www.ime.co.ir", "اختیار معامله بورس کالا",
                $"{PersianFormat.Interval(imeDerivatives.Value.Interval)}؛ گزارش هر روز عصرِ همان روز منتشر می‌شود"));
        // Inflation/CpiTable.cs: the Statistical Centre's CPI, added by hand each month.
        Sources.Add(new("مرکز آمار ایران", "https://www.amar.org.ir", "شاخص قیمت مصرف‌کننده (تورم)", "ماهانه، پس از انتشار گزارش مرکز آمار"));
    }
}
