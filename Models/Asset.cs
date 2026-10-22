namespace PortfolioManager.Api.Models;

public class Asset
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty; // ISIN or another unique name
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // e.g. Stock, ETF, Fund, Crypto, Gold, Currency
    public string? TsetmcInsCode { get; set; } // TSETMC's internal instrument code, set by the price sync job
    public string? Unit { get; set; } // what one price is for (e.g. «گرم», «مثقال», «عدد»); null when the app's default for the type fits
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // When the asset's past prices were fetched from its source and stored (see History/PriceHistoryBackfill.cs); null until a day before its first price is asked for
    public DateTime? HistoryLoadedAt { get; set; }

    public List<Price> Prices { get; set; } = new();
    public DerivativeContract? Contract { get; set; } // only for options and futures
}
