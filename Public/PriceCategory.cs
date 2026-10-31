using PortfolioManager.Api.Bitpin;
using PortfolioManager.Api.Fipiran;
using PortfolioManager.Api.Tgju;
using PortfolioManager.Api.Tsetmc;

namespace PortfolioManager.Api.Public;

/// <summary>
/// A group of asset types with a page of its own at /prices/{Slug}. Options and futures have none: there are thousands,
/// most of them short-lived.
/// </summary>
/// <param name="Title">The short name, on the category chips.</param>
/// <param name="Heading">The page's heading and the start of its title.</param>
/// <param name="ByName">Listed by symbol; otherwise tgju's instruments in the order of <see cref="TgjuInstruments.ByKey"/>
/// (dollar first, 18 karat gold first...) and the rest in the order they were added.</param>
/// <param name="ActiveDays">Only assets with a price this recent are listed, so delisted stocks and closed funds drop
/// off; null lists them all.</param>
/// <param name="IsPrice">Whether values are prices in Rial; index values are points.</param>
public sealed record PriceCategory(
    string Slug,
    string Title,
    string Heading,
    string Description,
    string[] Types,
    bool ByName = false,
    int? ActiveDays = 30,
    bool IsPrice = true)
{
    public string Path => $"/prices/{Slug}";

    public static readonly IReadOnlyList<PriceCategory> All =
    [
        new("currency", "ارز", "قیمت ارز",
            "قیمت روز دلار، یورو، درهم، لیر و دیگر ارزها در بازار آزاد، به ریال.",
            [TgjuInstruments.CurrencyType]),
        new("gold-coin", "طلا و سکه", "قیمت طلا و سکه",
            "قیمت روز طلای ۱۸ و ۲۴ عیار، مثقال، آبشده و سکهٔ امامی، بهار آزادی، نیم و ربع سکه، به ریال.",
            [TgjuInstruments.GoldType, TgjuInstruments.CoinType]),
        new("metal", "فلزات", "قیمت فلزات گران‌بها",
            "قیمت روز نقره، انس طلا، نقره، پلاتین و پالادیوم، به ریال.",
            [TgjuInstruments.MetalType]),
        new("crypto", "رمزارز", "قیمت رمزارزها",
            "قیمت روز بیت‌کوین، اتریوم، تتر و دیگر رمزارزها در بازار ایران، به ریال.",
            [BitpinPriceSyncService.CryptoType]),
        new("fund", "صندوق‌ها", "قیمت صندوق‌های سرمایه‌گذاری",
            "قیمت روز صندوق‌های قابل معامله در بورس و NAV صندوق‌های صدور و ابطال، به ریال.",
            [TsetmcPriceSyncService.EtfType, FipiranPriceSyncService.FundType], ByName: true),
        new("stock", "سهام", "قیمت سهام بورس و فرابورس",
            "قیمت پایانی روز سهام بورس تهران و فرابورس ایران، به ریال.",
            [TsetmcPriceSyncService.StockType], ByName: true),
        new("certificate", "گواهی و سلف", "قیمت گواهی سپرده و اوراق سلف",
            "قیمت روز گواهی‌های سپردهٔ کالایی (طلا، نقره و...) و اوراق سلف موازی، به ریال.",
            [TsetmcPriceSyncService.CertificateType, TsetmcPriceSyncService.SalafType], ByName: true),
        new("index", "شاخص و تورم", "شاخص کل بورس و تورم",
            "مقدار شاخص کل بورس تهران و شاخص قیمت مصرف‌کننده (تورم).",
            [TsetmcIndices.IndexType], ActiveDays: null, IsPrice: false),
    ];

    public static PriceCategory? Find(string slug) =>
        All.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
