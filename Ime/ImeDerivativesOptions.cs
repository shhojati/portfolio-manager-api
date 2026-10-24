namespace PortfolioManager.Api.Ime;

/// <summary>Settings for <see cref="ImeDerivativesSyncService"/>, bound from the "ImeDerivatives" section.</summary>
public sealed class ImeDerivativesOptions
{
    public const string SectionName = "ImeDerivatives";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often prices are fetched. IME's report is made once a day, in the evening after the trading day, so there
    /// are no market hours and nothing is gained by asking more often.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How many days back each run fetches, so days missed while the app or IME was down are filled in.</summary>
    public int LookbackDays { get; set; } = 7;
}
