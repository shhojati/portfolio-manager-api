namespace PortfolioManager.Api.Models;

public class Price
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public decimal Value { get; set; }
    public decimal? Nav { get; set; } // net asset value per unit, for funds (ETF redemption NAV)
    public DateTime Date { get; set; }

    public Asset? Asset { get; set; }
}
