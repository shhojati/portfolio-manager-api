namespace PortfolioManager.Api.Models;

// A TSETMC instrument the discovery job has looked up, so it is looked up only once. Discovery covers instruments the
// market watch leaves out (suspended ones, or ones that haven't traded for a while); most of those are rights, bonds or
// options, which are remembered too so they aren't looked up again every day. Whether one is imported, and as what,
// follows from its ISIN, so it is decided when it is used rather than stored.
public class TsetmcInstrument
{
    public string InsCode { get; set; } = string.Empty;
    public string? Isin { get; set; } // null if TSETMC didn't know the code
    public string? Symbol { get; set; }
    public string? Name { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
