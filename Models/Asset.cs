namespace PortfolioManager.Api.Models;

public class Asset
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty; // ISIN or another unique name
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // e.g. Stock, Crypto, Gold, Currency
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Price> Prices { get; set; } = new();
}
