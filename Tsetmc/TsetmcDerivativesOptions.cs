namespace PortfolioManager.Api.Tsetmc;

/// <summary>Settings for <see cref="TsetmcDerivativesSyncService"/>, bound from the "TsetmcDerivatives" section. Times are Tehran time.</summary>
public sealed class TsetmcDerivativesOptions
{
    public const string SectionName = "TsetmcDerivatives";

    public bool Enabled { get; set; } = true;

    /// <summary>Sync once when the app starts, even outside market hours.</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>
    /// How often prices are fetched while the market is open. Each run is two requests for the whole market, so it can
    /// be short; option premiums move a lot more than share prices.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Days the job is active. Empty means every day.</summary>
    public DayOfWeek[] TradingDays { get; set; } = [];

    public TimeOnly MarketOpen { get; set; } = new(8, 45);
    public TimeOnly MarketClose { get; set; } = new(13, 0);

    /// <summary>
    /// Maximum parallel requests when looking up what the market watches leave out: the ISIN of an option not traded
    /// today, the contract size of a new future. Only the first run makes many (a few hundred ISINs).
    /// </summary>
    public int LookupConcurrency { get; set; } = 4;
}
