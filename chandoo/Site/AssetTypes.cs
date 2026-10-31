namespace Chandoo.Site;

/// <summary>The asset types the Portfolio Manager API uses (the "type" of each asset).</summary>
public static class AssetTypes
{
    public const string Currency = "Currency";
    public const string Gold = "Gold";
    public const string Coin = "Coin";
    public const string Metal = "Metal";
    public const string Crypto = "Crypto";
    public const string Etf = "ETF";
    public const string Fund = "Fund";
    public const string Stock = "Stock";
    public const string Certificate = "Certificate";
    public const string Salaf = "Salaf";
    public const string Index = "Index";
    public const string Option = "Option";
    public const string Future = "Future";

    /// <summary>The unit the API gives indices («واحد»), which says nothing worth showing.</summary>
    public const string IndexUnit = "واحد";
}
