namespace PortfolioManager.Api.Models;

// A day whose fund NAVs have been fetched from FIPIRAN and stored as prices (see History/PriceHistoryBackfill.cs), so it
// is fetched only once. FIPIRAN answers with every fund's NAV as of one day, so days are kept rather than funds.
public class FipiranDay
{
    public DateOnly Date { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
