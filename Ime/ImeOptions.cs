namespace PortfolioManager.Api.Ime;

/// <summary>Settings for <see cref="ImePriceSyncService"/>, bound from the "Ime" section.</summary>
public sealed class ImeOptions
{
    public const string SectionName = "Ime";

    public bool Enabled { get; set; } = true;

    /// <summary>How often prices are fetched. Each run is one request per certificate, so there are no market hours.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How many days back each run fetches, so days missed while the app or IME was down are filled in. Raise it for a
    /// one-off import of older prices (IME has them from 1401/12, when the certificates were launched).
    /// </summary>
    public int LookbackDays { get; set; } = 7;
}
