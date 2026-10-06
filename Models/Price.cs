namespace PortfolioManager.Api.Models;

public class Price
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public decimal Value { get; set; }
    // What the asset is worth underneath, per unit, so its bubble can be shown: a fund's net asset value (ETF
    // redemption NAV), or the value of the gold in a coin or a mesghal at the world price (TgjuInstrument.Intrinsic).
    public decimal? Nav { get; set; }
    public DateTime Date { get; set; }

    public Asset? Asset { get; set; }
}
