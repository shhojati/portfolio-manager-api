namespace PortfolioManager.Api.Tgju;

/// <summary>Settings for <see cref="TgjuPriceSyncService"/>, bound from the "Tgju" section.</summary>
public sealed class TgjuOptions
{
    public const string SectionName = "Tgju";

    public bool Enabled { get; set; } = true;

    /// <summary>How often prices are fetched. tgju updates the major currencies every few minutes.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Quotes tgju hasn't updated for longer than this are ignored, so currencies it stopped tracking don't get stale prices.</summary>
    public TimeSpan MaxQuoteAge { get; set; } = TimeSpan.FromDays(7);
}
