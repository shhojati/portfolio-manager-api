namespace PortfolioManager.Api.Tsetmc;

/// <summary>
/// The market indices imported as assets, so the app can chart them like any other: for now only the TSE's total index
/// (شاخص کل), for comparing a portfolio's return with the market's. <see cref="TsetmcPriceSyncService"/> keeps its
/// value per trading day; its past values come from TSETMC's index history (see History/PriceHistoryBackfill.cs).
/// </summary>
public static class TsetmcIndices
{
    public const string IndexType = "Index";

    /// <summary>The total index's TSETMC code; indices have no ISIN.</summary>
    public const string TotalInsCode = "32097828799138957";
    public const string TotalIdentifier = "TSE-INDEX";
    public const string TotalSymbol = "شاخص کل";
    public const string TotalName = "شاخص کل بورس تهران";
    public const string Unit = "واحد";
}
