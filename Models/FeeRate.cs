namespace PortfolioManager.Api.Models;

/// <summary>
/// What brokers charge on a trade of one kind of exchange-traded asset (see Fees.FeeClasses), as a percent of its value,
/// from <see cref="EffectiveFrom"/> until the class's next rate. A trade is charged the rate in effect on its own day,
/// so changing the rate from some day on leaves the fees of earlier trades as they were.
/// </summary>
public class FeeRate
{
    public int Id { get; set; }
    public string Class { get; set; } = string.Empty; // see Fees.FeeClasses
    public DateOnly EffectiveFrom { get; set; }
    public decimal BuyPercent { get; set; }
    public decimal SellPercent { get; set; } // includes the 0.5% tax on selling shares
}
