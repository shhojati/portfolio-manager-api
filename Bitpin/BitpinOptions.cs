namespace PortfolioManager.Api.Bitpin;

/// <summary>Settings for <see cref="BitpinPriceSyncService"/>, bound from the "Bitpin" section.</summary>
public sealed class BitpinOptions
{
    public const string SectionName = "Bitpin";

    public bool Enabled { get; set; } = true;

    /// <summary>How often prices are fetched. Crypto trades around the clock, so there are no market hours.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);
}
