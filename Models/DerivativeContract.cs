namespace PortfolioManager.Api.Models;

// The terms of an option or futures contract, kept beside its Asset (one each). The asset's prices are per unit of the
// underlying, as the exchange quotes them, so one contract is worth price × ContractSize. Positions (long or short,
// margin) are the client app's business; this only says what the contract is.
public class DerivativeContract
{
    public int AssetId { get; set; }
    public string Kind { get; set; } = string.Empty; // see DerivativeKinds
    public decimal? StrikePrice { get; set; } // Rial per unit of the underlying; null for a future
    public DateOnly ExpiryDate { get; set; }
    public int ContractSize { get; set; } // units of the underlying per contract
    // The underlying asset, once it has been imported; UnderlyingSymbol says what it is until then
    public int? UnderlyingAssetId { get; set; }
    public string UnderlyingSymbol { get; set; } = string.Empty;

    public Asset? Asset { get; set; }
    public Asset? UnderlyingAsset { get; set; }
}

public static class DerivativeKinds
{
    public const string Call = "Call";
    public const string Put = "Put";
    public const string Future = "Future";
}
