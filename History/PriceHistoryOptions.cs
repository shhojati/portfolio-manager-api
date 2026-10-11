namespace PortfolioManager.Api.History;

/// <summary>Settings for <see cref="PriceHistoryBackfill"/>, bound from the "PriceHistory" section.</summary>
public sealed class PriceHistoryOptions
{
    public const string SectionName = "PriceHistory";

    /// <summary>Fetch past prices that aren't stored yet when a history request asks for them.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a history request waits for the prices it started fetching before answering with what is stored. The
    /// fetches go on after that, so asking again a little later gets the rest.
    /// </summary>
    public TimeSpan WaitTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>After a failed fetch, how long until the same asset or day is tried again.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Parallel requests to each of TSETMC, Bitpin, tgju and IME. FIPIRAN days are fetched one at a time.</summary>
    public int Concurrency { get; set; } = 2;

    /// <summary>
    /// No prices are asked for before this day. It bounds how far back Bitpin is asked, and how many days of fund NAVs
    /// FIPIRAN can be asked for, one request each.
    /// </summary>
    public DateOnly EarliestDate { get; set; } = new(2010, 1, 1);
}
