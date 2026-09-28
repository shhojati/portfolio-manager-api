namespace PortfolioManager.Api.Tsetmc;

/// <summary>Settings for <see cref="TsetmcPriceSyncService"/>, bound from the "Tsetmc" section. Times are Tehran time.</summary>
public sealed class TsetmcOptions
{
    public const string SectionName = "Tsetmc";

    public bool Enabled { get; set; } = true;

    /// <summary>Sync once when the app starts, even outside market hours.</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>How often prices are fetched while the market is open.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Days the job is active. Empty means every day.</summary>
    public DayOfWeek[] TradingDays { get; set; } = [];

    public TimeOnly MarketOpen { get; set; } = new(8, 45);
    public TimeOnly MarketClose { get; set; } = new(13, 0);

    /// <summary>Also fetch ETF NAVs (one request per ETF, so on a slower schedule than prices).</summary>
    public bool SyncEtfNav { get; set; } = true;
    public TimeSpan NavInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Funds keep publishing NAV after trading ends, so NAV syncing runs until this time.</summary>
    public TimeOnly NavClose { get; set; } = new(18, 30);

    /// <summary>Maximum parallel NAV requests, kept low to stay polite to TSETMC.</summary>
    public int NavConcurrency { get; set; } = 4;
}
