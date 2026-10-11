namespace PortfolioManager.Api.Ime;

/// <summary>
/// A continuous deposit certificate (گواهی سپرده پیوسته) and the <see cref="Models.Asset"/> it is imported as.
/// <see cref="Code"/> is IME's contract code, stored as the asset's identifier; <see cref="Site"/> is the IME site whose
/// reports it is on. <see cref="Unit"/> names what one certificate, and so one stored price, is for.
/// </summary>
public sealed record ImeCertificate(string Code, string Symbol, string Name, string Unit, Uri Site);

/// <summary>
/// The commodity exchange's continuous deposit certificates. Unlike its other deposit certificates, which are issued
/// per harvest or batch and are imported from TSETMC, these never mature, and TSETMC doesn't list them.
/// </summary>
public static class ImeCertificates
{
    private static readonly Uri GoldSite = new("https://gold.ime.co.ir/");
    private static readonly Uri SilverSite = new("https://silver.ime.co.ir/");

    public static readonly IReadOnlyList<ImeCertificate> All =
    [
        // One certificate is 100 sut (0.1 g) of a 1 kg bar of 995 gold, so its price is a tenth of a gram's.
        new("CD1GOB0001", "گواهی شمش طلا", "گواهی سپرده پیوسته شمش طلای ۹۹۵", "۰٫۱ گرم", GoldSite),
        new("CD1GOC0001", "گواهی سکه", "گواهی سپرده پیوسته تمام سکه بهار آزادی طرح جدید", "عدد", GoldSite),
        new("CD1SIB0001", "گواهی شمش نقره", "گواهی سپرده پیوسته شمش نقره ۹۹۹٫۹", "گرم", SilverSite),
    ];

    /// <summary>The first day IME has these certificates' prices for (1401/12/01); their whole history is asked for from here.</summary>
    public static readonly DateOnly HistoryStart = new(2023, 2, 20);

    public static ImeCertificate? ByCode(string code) => All.FirstOrDefault(c => c.Code == code);

    /// <summary>
    /// A certificate's price on each trading day of <paramref name="trades"/>: the day's settlement price, the exchange's
    /// official price for it, or the last trade's for a day IME hasn't settled yet.
    /// </summary>
    public static IEnumerable<(DateOnly Date, decimal Value)> DailyPrices(ImeCertificate certificate, IEnumerable<CertificateTrade> trades) =>
        trades
            .Where(t => t.ContractCode is null || t.ContractCode.Trim() == certificate.Code)
            .Select(t => (Trade: t, Value: t.TodaySettlementPrice is > 0 ? t.TodaySettlementPrice.Value : t.LastPrice ?? 0))
            .Where(t => t.Value > 0 && t.Trade.Date is not null)
            .Select(t => (DateOnly.FromDateTime(t.Trade.Date!.Value), t.Value))
            .DistinctBy(p => p.Item1);
}
