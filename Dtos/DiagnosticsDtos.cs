namespace PortfolioManager.Api.Dtos;

/// <summary>
/// What happened when the server asked tgju's site API for an instrument's daily history (GET
/// /api/diagnostics/tgju-history): whether the host resolved and answered, how the answer parsed, and the live
/// feed's price for the same instrument to compare the newest close with. Fields are null when that step wasn't
/// reached or failed; <see cref="Error"/> and <see cref="LiveError"/> say why.
/// </summary>
public record TgjuHistoryCheckDto(
    string Key,
    string Url,
    List<string> Addresses,
    string? DnsError,
    int? StatusCode,
    long ElapsedMs,
    string? ContentType,
    long? Bytes,
    string? Error,
    int? RecordsTotal,
    int? Rows,
    int? ParsedDays,
    DayPriceDto? Oldest,
    DayPriceDto? Newest,
    List<string> SampleRows,
    decimal? LivePrice,
    DateTime? LiveTehranTime,
    string? LiveError);
