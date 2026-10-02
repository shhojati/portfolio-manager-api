namespace PortfolioManager.Api.Tgju;

/// <summary>
/// An instrument in tgju's feed and the <see cref="Models.Asset"/> it is imported as. <see cref="Code"/> is a stable Latin
/// code (ISO 4217 for currencies) stored as the asset's identifier; <see cref="Symbol"/> is the short Persian name shown
/// to users (at most 20 characters) and <see cref="Name"/> the full one. The price tgju quotes is for <see cref="Units"/>
/// units, in Rial, or in US dollars when <see cref="InUsd"/> is set (world metal prices).
/// </summary>
public sealed record TgjuInstrument(string Code, string Symbol, string Name, string Type, int Units = 1, bool InUsd = false);

/// <summary>
/// Everything imported from tgju, keyed by tgju's name for it: world currencies, Iranian gold and coins, and metals.
/// tgju's feed also holds crypto (price_eos, price_bch...), dead currencies (price_skk, price_cyp...), old dollar
/// indicators, bubbles, options and hundreds of commodities, which are left out here.
/// </summary>
public static class TgjuInstruments
{
    public const string CurrencyType = "Currency";
    public const string GoldType = "Gold";
    public const string CoinType = "Coin";
    public const string MetalType = "Metal";

    /// <summary>tgju's key for the free-market US dollar, used to convert <see cref="TgjuInstrument.InUsd"/> prices to Rial.</summary>
    public const string UsdKey = "price_dollar_rl";

    // A currency's symbol is its Persian name, unless that is too long for a symbol.
    private static TgjuInstrument Currency(string code, string name, int units = 1, string? symbol = null) =>
        new(code, symbol ?? name, name, CurrencyType, units);

    public static readonly IReadOnlyDictionary<string, TgjuInstrument> ByKey = new Dictionary<string, TgjuInstrument>(StringComparer.Ordinal)
    {
        // Gold, in Rial per gram (per mesghal, 4.608 g of 17 karat, for melted gold)
        ["geram18"] = new("GOLD-18", "طلای ۱۸ عیار", "طلای ۱۸ عیار ۷۵۰ (گرم)", GoldType),
        ["gold_740k"] = new("GOLD-18-740", "طلای ۱۸ عیار ۷۴۰", "طلای ۱۸ عیار ۷۴۰ (گرم)", GoldType),
        ["geram24"] = new("GOLD-24", "طلای ۲۴ عیار", "طلای ۲۴ عیار (گرم)", GoldType),
        ["mesghal"] = new("GOLD-MESGHAL", "مثقال طلا", "مثقال طلا", GoldType),
        ["gold_futures"] = new("GOLD-MELTED", "آبشده نقدی", "طلای آبشده نقدی (مثقال)", GoldType),
        ["gold_world_futures"] = new("GOLD-MELTED-SUB1KG", "آبشده کمتر از کیلو", "طلای آبشده کمتر از کیلو (مثقال)", GoldType),
        ["gold_melted_wholesale"] = new("GOLD-MELTED-BULK", "آبشده بنکداری", "طلای آبشده بنکداری (مثقال)", GoldType),

        // Gold coins, in Rial per coin
        ["sekee"] = new("COIN-EMAMI", "سکه امامی", "سکه امامی", CoinType),
        ["sekeb"] = new("COIN-BAHAR", "سکه بهار آزادی", "سکه بهار آزادی", CoinType),
        ["nim"] = new("COIN-NIM", "نیم سکه", "نیم سکه", CoinType),
        ["rob"] = new("COIN-ROB", "ربع سکه", "ربع سکه", CoinType),
        ["gerami"] = new("COIN-GERAMI", "سکه گرمی", "سکه گرمی", CoinType),
        ["sekee_down"] = new("COIN-EMAMI-OLD", "تمام سکه قبل ۸۶", "تمام سکه (قبل ۸۶)", CoinType),
        ["nim_down"] = new("COIN-NIM-OLD", "نیم سکه قبل ۸۶", "نیم سکه (قبل ۸۶)", CoinType),
        ["rob_down"] = new("COIN-ROB-OLD", "ربع سکه قبل ۸۶", "ربع سکه (قبل ۸۶)", CoinType),

        // Silver on the Iranian market, in Rial per gram
        ["silver_999"] = new("SILVER-999", "نقره ۹۹۹", "نقره ۹۹۹ (گرم)", MetalType),
        ["silver_925"] = new("SILVER-925", "نقره ۹۲۵", "نقره ۹۲۵ (گرم)", MetalType),

        // World precious metals, in dollars per troy ounce
        ["ons"] = new("XAU", "انس طلا", "انس طلا", MetalType, InUsd: true),
        ["silver"] = new("XAG", "انس نقره", "انس نقره", MetalType, InUsd: true),
        ["platinum"] = new("XPT", "انس پلاتین", "انس پلاتین", MetalType, InUsd: true),
        ["palladium"] = new("XPD", "انس پالادیوم", "انس پالادیوم", MetalType, InUsd: true),

        // World base metals, in dollars per tonne
        ["base_global_copper"] = new("COPPER", "مس جهانی", "مس (تن)", MetalType, InUsd: true),
        ["aluminium"] = new("ALUMINIUM", "آلومینیوم جهانی", "آلومینیوم (تن)", MetalType, InUsd: true),
        ["base_global_zinc"] = new("ZINC", "روی جهانی", "روی (تن)", MetalType, InUsd: true),
        ["base_global_nickel"] = new("NICKEL", "نیکل جهانی", "نیکل (تن)", MetalType, InUsd: true),
        ["base_global_lead"] = new("LEAD", "سرب جهانی", "سرب (تن)", MetalType, InUsd: true),
        ["base_global_tin"] = new("TIN", "قلع جهانی", "قلع (تن)", MetalType, InUsd: true),

        // World currencies, in Rial per unit. A few keys still use the ISO code from before a redenomination but
        // carry the current currency's price, so they are mapped to the current code (price_mro is the new ouguiya, MRU).
        [UsdKey] = Currency("USD", "دلار آمریکا"),
        ["price_eur"] = Currency("EUR", "یورو"),
        ["price_gbp"] = Currency("GBP", "پوند انگلیس"),
        ["price_chf"] = Currency("CHF", "فرانک سوئیس"),
        ["price_cad"] = Currency("CAD", "دلار کانادا"),
        ["price_aud"] = Currency("AUD", "دلار استرالیا"),
        ["price_nzd"] = Currency("NZD", "دلار نیوزیلند"),
        ["price_jpy"] = Currency("JPY", "ین ژاپن", units: 100),
        ["price_cny"] = Currency("CNY", "یوان چین"),
        ["price_hkd"] = Currency("HKD", "دلار هنگ‌کنگ"),
        ["price_sgd"] = Currency("SGD", "دلار سنگاپور"),
        ["price_krw"] = Currency("KRW", "وون کره جنوبی"),
        ["price_twd"] = Currency("TWD", "دلار تایوان"),
        ["price_mop"] = Currency("MOP", "پاتاکا ماکائو"),

        // Middle East and Iran's neighbours
        ["price_aed"] = Currency("AED", "درهم امارات"),
        ["price_try"] = Currency("TRY", "لیر ترکیه"),
        ["price_iqd"] = Currency("IQD", "دینار عراق"),
        ["price_afn"] = Currency("AFN", "افغانی افغانستان"),
        ["price_pkr"] = Currency("PKR", "روپیه پاکستان"),
        ["price_sar"] = Currency("SAR", "ریال عربستان"),
        ["price_qar"] = Currency("QAR", "ریال قطر"),
        ["price_kwd"] = Currency("KWD", "دینار کویت"),
        ["price_bhd"] = Currency("BHD", "دینار بحرین"),
        ["price_omr"] = Currency("OMR", "ریال عمان"),
        ["price_jod"] = Currency("JOD", "دینار اردن"),
        ["price_syp"] = Currency("SYP", "پوند سوریه"),
        ["price_lbp"] = Currency("LBP", "پوند لبنان"),
        ["price_yer"] = Currency("YER", "ریال یمن"),
        ["price_azn"] = Currency("AZN", "منات آذربایجان"),
        ["price_amd"] = Currency("AMD", "درام ارمنستان"),
        ["price_gel"] = Currency("GEL", "لاری گرجستان"),
        ["price_tmt"] = Currency("TMT", "منات ترکمنستان"),

        // Central Asia and Eastern Europe
        ["price_rub"] = Currency("RUB", "روبل روسیه"),
        ["price_uah"] = Currency("UAH", "گریونا اوکراین"),
        ["price_byn"] = Currency("BYN", "روبل بلاروس"),
        ["price_kzt"] = Currency("KZT", "تنگه قزاقستان"),
        ["price_kgs"] = Currency("KGS", "سوم قرقیزستان"),
        ["price_tjs"] = Currency("TJS", "سامانی تاجیکستان"),
        ["price_uzs"] = Currency("UZS", "سوم ازبکستان"),
        ["price_mdl"] = Currency("MDL", "لئو مولداوی"),
        ["price_all"] = Currency("ALL", "لک آلبانی"),
        ["price_bam"] = Currency("BAM", "مارک بوسنی و هرزگوین"),
        ["price_bgn"] = Currency("BGN", "لف بلغارستان"),
        ["price_mkd"] = Currency("MKD", "دینار مقدونیه شمالی"),
        ["price_rsd"] = Currency("RSD", "دینار صربستان"),
        ["price_ron"] = Currency("RON", "لئو رومانی"),
        ["price_huf"] = Currency("HUF", "فورینت مجارستان"),
        ["price_czk"] = Currency("CZK", "کرون چک"),
        ["price_pln"] = Currency("PLN", "زلوتی لهستان"),

        // Nordics
        ["price_dkk"] = Currency("DKK", "کرون دانمارک"),
        ["price_sek"] = Currency("SEK", "کرون سوئد"),
        ["price_nok"] = Currency("NOK", "کرون نروژ"),
        ["price_isk"] = Currency("ISK", "کرون ایسلند"),

        // South and Southeast Asia
        ["price_inr"] = Currency("INR", "روپیه هند"),
        ["price_bdt"] = Currency("BDT", "تاکا بنگلادش"),
        ["price_btn"] = Currency("BTN", "انگولتروم بوتان"),
        ["price_npr"] = Currency("NPR", "روپیه نپال"),
        ["price_lkr"] = Currency("LKR", "روپیه سریلانکا"),
        ["price_mvr"] = Currency("MVR", "روفیه مالدیو"),
        ["price_thb"] = Currency("THB", "بات تایلند"),
        ["price_myr"] = Currency("MYR", "رینگیت مالزی"),
        ["price_idr"] = Currency("IDR", "روپیه اندونزی"),
        ["price_php"] = Currency("PHP", "پزو فیلیپین"),
        ["price_vnd"] = Currency("VND", "دانگ ویتنام"),
        ["price_lak"] = Currency("LAK", "کیپ لائوس"),
        ["price_khr"] = Currency("KHR", "ریل کامبوج"),
        ["price_mmk"] = Currency("MMK", "کیات میانمار"),

        // Oceania
        ["price_fjd"] = Currency("FJD", "دلار فیجی"),
        ["price_pgk"] = Currency("PGK", "کینا پاپوآ گینه نو"),
        ["price_vuv"] = Currency("VUV", "واتو وانواتو"),
        ["price_xpf"] = Currency("XPF", "فرانک اقیانوسیه"),

        // Americas
        ["price_brl"] = Currency("BRL", "رئال برزیل"),
        ["price_ars"] = Currency("ARS", "پزو آرژانتین"),
        ["price_clp"] = Currency("CLP", "پزو شیلی"),
        ["price_cop"] = Currency("COP", "پزو کلمبیا"),
        ["price_pen"] = Currency("PEN", "سول پرو"),
        ["price_pyg"] = Currency("PYG", "گوارانی پاراگوئه"),
        ["price_uyu"] = Currency("UYU", "پزو اروگوئه"),
        ["price_vef"] = Currency("VES", "بولیوار ونزوئلا"),
        ["price_mxn"] = Currency("MXN", "پزو مکزیک"),
        ["price_gtq"] = Currency("GTQ", "کتزال گواتمالا"),
        ["price_hnl"] = Currency("HNL", "لمپیرا هندوراس"),
        ["price_nio"] = Currency("NIO", "کوردوبا نیکاراگوئه"),
        ["price_crc"] = Currency("CRC", "کولون کاستاریکا"),
        ["price_svc"] = Currency("SVC", "کولون السالوادور"),
        ["price_pab"] = Currency("PAB", "بالبوآ پاناما"),
        ["price_dop"] = Currency("DOP", "پزو دومینیکن"),
        ["price_cup"] = Currency("CUP", "پزو کوبا"),
        ["price_htg"] = Currency("HTG", "گورد هائیتی"),
        ["price_jmd"] = Currency("JMD", "دلار جامائیکا"),
        ["price_ttd"] = Currency("TTD", "دلار ترینیداد و توباگو", symbol: "دلار ترینیداد"),
        ["price_bbd"] = Currency("BBD", "دلار باربادوس"),
        ["price_bsd"] = Currency("BSD", "دلار باهاما"),
        ["price_bzd"] = Currency("BZD", "دلار بلیز"),
        ["price_xcd"] = Currency("XCD", "دلار کارائیب شرقی"),
        ["price_kyd"] = Currency("KYD", "دلار جزایر کیمن"),
        ["price_awg"] = Currency("AWG", "فلورین آروبا"),
        ["price_ang"] = Currency("ANG", "گیلدر آنتیل هلند"),
        ["price_gyd"] = Currency("GYD", "دلار گویان"),

        // Africa
        ["price_egp"] = Currency("EGP", "پوند مصر"),
        ["price_lyd"] = Currency("LYD", "دینار لیبی"),
        ["price_tnd"] = Currency("TND", "دینار تونس"),
        ["price_dzd"] = Currency("DZD", "دینار الجزایر"),
        ["price_mad"] = Currency("MAD", "درهم مراکش"),
        ["price_sdg"] = Currency("SDG", "پوند سودان"),
        ["price_zar"] = Currency("ZAR", "راند آفریقای جنوبی"),
        ["price_nad"] = Currency("NAD", "دلار نامیبیا"),
        ["price_lsl"] = Currency("LSL", "لوتی لسوتو"),
        ["price_szl"] = Currency("SZL", "لیلانگنی اسواتینی"),
        ["price_bwp"] = Currency("BWP", "پولا بوتسوانا"),
        ["price_zmw"] = Currency("ZMW", "کواچا زامبیا"),
        ["price_mwk"] = Currency("MWK", "کواچا مالاوی"),
        ["price_mzn"] = Currency("MZN", "متیکال موزامبیک"),
        ["price_ghs"] = Currency("GHS", "سدی غنا"),
        ["price_ngn"] = Currency("NGN", "نایرا نیجریه"),
        ["price_xof"] = Currency("XOF", "فرانک غرب آفریقا"),
        ["price_xaf"] = Currency("XAF", "فرانک مرکز آفریقا"),
        ["price_cdf"] = Currency("CDF", "فرانک کنگو"),
        ["price_etb"] = Currency("ETB", "بیر اتیوپی"),
        ["price_kes"] = Currency("KES", "شیلینگ کنیا"),
        ["price_ugx"] = Currency("UGX", "شیلینگ اوگاندا"),
        ["price_tzs"] = Currency("TZS", "شیلینگ تانزانیا"),
        ["price_rwf"] = Currency("RWF", "فرانک رواندا"),
        ["price_bif"] = Currency("BIF", "فرانک بوروندی"),
        ["price_sos"] = Currency("SOS", "شیلینگ سومالی"),
        ["price_djf"] = Currency("DJF", "فرانک جیبوتی"),
        ["price_kmf"] = Currency("KMF", "فرانک کومور"),
        ["price_mga"] = Currency("MGA", "آریاری ماداگاسکار"),
        ["price_mur"] = Currency("MUR", "روپیه موریس"),
        ["price_scr"] = Currency("SCR-CURRENCY", "روپیه سیشل"), // "SCR" is already the identifier of the Scroll crypto coin
        ["price_cve"] = Currency("CVE", "اسکودو کیپ ورد"),
        ["price_gmd"] = Currency("GMD", "دالاسی گامبیا"),
        ["price_gnf"] = Currency("GNF", "فرانک گینه"),
        ["price_lrd"] = Currency("LRD", "دلار لیبریا"),
        ["price_mro"] = Currency("MRU", "اوگیه موریتانی"),
        ["price_std"] = Currency("STN", "دوبرا سائوتومه و پرنسیپ", symbol: "دوبرای سائوتومه"),
        ["price_shp"] = Currency("SHP", "پوند سنت هلن"),
    };
}
