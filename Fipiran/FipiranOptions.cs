namespace PortfolioManager.Api.Fipiran;

/// <summary>Settings for <see cref="FipiranPriceSyncService"/>, bound from the "Fipiran" section.</summary>
public sealed class FipiranOptions
{
    public const string SectionName = "Fipiran";

    public bool Enabled { get; set; } = true;

    /// <summary>How often NAVs are fetched. Funds publish one NAV a day, at no fixed time, so there are no market hours.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Funds whose latest NAV is older than this are ignored, so funds that stopped reporting (or were liquidated) aren't imported.</summary>
    public TimeSpan MaxNavAge { get; set; } = TimeSpan.FromDays(7);
}
