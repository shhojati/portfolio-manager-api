namespace Chandoo.Site;

/// <summary>
/// A group of asset types with a page of its own at /prices/{Slug}. Options and futures have none: there are thousands,
/// most of them short-lived.
/// </summary>
/// <param name="Title">The short name, on the category chips.</param>
/// <param name="Heading">The page's heading and the start of its title.</param>
/// <param name="ByName">Listed by symbol; otherwise in the API's own order (dollar first, 18 karat gold first...).</param>
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
            [AssetTypes.Currency]),
        new("gold-coin", "طلا و سکه", "قیمت طلا و سکه",
            "قیمت روز طلای ۱۸ و ۲۴ عیار، مثقال، آبشده و سکهٔ امامی، بهار آزادی، نیم و ربع سکه، به ریال.",
            [AssetTypes.Gold, AssetTypes.Coin]),
        new("metal", "فلزات", "قیمت فلزات گران‌بها",
            "قیمت روز نقره، انس طلا، نقره، پلاتین و پالادیوم، به ریال.",
            [AssetTypes.Metal]),
        new("crypto", "رمزارز", "قیمت رمزارزها",
            "قیمت روز بیت‌کوین، اتریوم، تتر و دیگر رمزارزها در بازار ایران، به ریال.",
            [AssetTypes.Crypto]),
        new("fund", "صندوق‌ها", "قیمت صندوق‌های سرمایه‌گذاری",
            "قیمت روز صندوق‌های قابل معامله در بورس و NAV صندوق‌های صدور و ابطال، به ریال.",
            [AssetTypes.Etf, AssetTypes.Fund], ByName: true),
        new("stock", "سهام", "قیمت سهام بورس و فرابورس",
            "قیمت پایانی روز سهام بورس تهران و فرابورس ایران، به ریال.",
            [AssetTypes.Stock], ByName: true),
        new("certificate", "گواهی و سلف", "قیمت گواهی سپرده و اوراق سلف",
            "قیمت روز گواهی‌های سپردهٔ کالایی (طلا، نقره و...) و اوراق سلف موازی، به ریال.",
            [AssetTypes.Certificate, AssetTypes.Salaf], ByName: true),
        new("commodity-exchange", "بورس کالا", "قیمت‌های بورس کالا",
            "قیمت‌های بورس کالا امروز؛ آخرین قیمت گواهی سپرده کالایی طلا، سکه و نقره و اوراق سلف موجود در چندو، به ریال، همراه با تغییرات روزانه و روند اخیر.",
            [AssetTypes.Certificate, AssetTypes.Salaf], ByName: true),
        new("index", "شاخص و تورم", "شاخص کل بورس و تورم",
            "مقدار شاخص کل بورس تهران و شاخص قیمت مصرف‌کننده (تورم).",
            [AssetTypes.Index], ActiveDays: null, IsPrice: false),
    ];

    public static PriceCategory? Find(string slug) =>
        All.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
