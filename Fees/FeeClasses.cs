namespace PortfolioManager.Api.Fees;

/// <summary>
/// The kinds of exchange-traded asset that have fee rates of their own. The mobile app tells which one an asset is from
/// its type and ISIN (IRO1 is the TSE, the rest Fara Bourse and its base market), so the keys are fixed: the admin
/// changes their rates, not the list. Gold, coins, currency and crypto bought outside the exchange, issue-and-redeem
/// funds, futures and the user's own assets have no exchange fee.
/// </summary>
public static class FeeClasses
{
    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("stockTse", "سهام بورس"),
        ("stockIfb", "سهام فرابورس و بازار پایه"),
        ("etf", "صندوق قابل معامله"),
        ("option", "اختیار معامله"),
        ("certificate", "گواهی سپرده و سلف"),
    ];

    public static bool IsValid(string key) => All.Any(c => c.Key == key);
}
