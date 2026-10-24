using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PortfolioManager.Api.Ime;

/// <summary>
/// Typed client for the option report behind www.ime.co.ir (the commodity exchange's own site): every option contract's
/// trading on each day, as on the site's derivatives report page. The API is free but unofficial and undocumented.
/// It is a daily report, made in the evening after the trading day, not a live board. Prices are in Rial per unit of
/// the underlying. IME's servers seem to answer only Iranian IPs.
/// </summary>
public sealed partial class ImeDerivativesClient(HttpClient http)
{
    private static readonly PersianCalendar Persian = new();

    /// <summary>The open option contracts' daily summaries between two days (inclusive), one row per contract per trading day.</summary>
    public async Task<IReadOnlyList<ImeOptionDay>> GetOptionDaysAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        // c=-1 is open contracts only; ot=0 both calls and puts; lang=8 Persian.
        var url = $"subsystems/ime/option/optionboarddata.ashx?f={ToJalali(from)}&t={ToJalali(to)}&c=-1&ot=0&lang=8&order=asc";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Referrer = new Uri("https://www.ime.co.ir/fut-report.html");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<OptionReport>(ct);
        return report?.Rows ?? [];
    }

    /// <summary>
    /// A contract's terms, from its description, which is the only place IME gives them: «قرارداد اختیار معامله خرید
    /// شمش طلا سررسید بهمن ماه 1405 با قیمت اعمال 22,000,000 ریال» is a call on شمش طلا with a strike of 22,000,000.
    /// Null if the description isn't in that shape.
    /// </summary>
    public static ImeOptionTerms? ParseTerms(string? description)
    {
        var match = TermsPattern().Match((description ?? string.Empty).Replace('ي', 'ی').Replace('ك', 'ک'));
        if (!match.Success || !decimal.TryParse(match.Groups["strike"].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var strike))
            return null;

        var underlying = string.Join(' ', match.Groups["underlying"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return new ImeOptionTerms(match.Groups["kind"].Value == "خرید", underlying, strike);
    }

    /// <summary>A Jalali yyyy/MM/dd date (IME's own format) as a Gregorian date; null if it isn't one.</summary>
    public static DateOnly? ParseJalali(string? text)
    {
        var parts = (text ?? string.Empty).Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m) || !int.TryParse(parts[2], out var d))
            return null;

        try
        {
            return DateOnly.FromDateTime(Persian.ToDateTime(y, m, d, 0, 0, 0, 0));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string ToJalali(DateOnly date)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        return $"{Persian.GetYear(day):0000}/{Persian.GetMonth(day):00}/{Persian.GetDayOfMonth(day):00}";
    }

    [GeneratedRegex(@"اختیار معامله (?<kind>خرید|فروش) (?<underlying>.+?) سررسید .*قیمت اعمال (?<strike>[\d,]+) ریال")]
    private static partial Regex TermsPattern();

    private sealed record OptionReport(List<ImeOptionDay>? Rows);
}

/// <summary>
/// A contract's trading on one day. <see cref="Date"/> is the trading day. <see cref="TodaySettlementPrice"/> is the
/// day's settlement price, the exchange's official price for the day; <see cref="LastPrice"/> the last trade's (0 if
/// it didn't trade). <see cref="DeliveryDate"/> is its expiry, a Jalali yyyy/MM/dd date.
/// </summary>
public sealed record ImeOptionDay(
    [property: JsonPropertyName("ContractCode")] string? ContractCode,
    [property: JsonPropertyName("ContractDescription")] string? ContractDescription,
    [property: JsonPropertyName("DT_en")] DateTime? Date,
    [property: JsonPropertyName("DeliveryDate")] string? DeliveryDate,
    [property: JsonPropertyName("LastPrice")] decimal? LastPrice,
    [property: JsonPropertyName("TodaySettlementPrice")] decimal? TodaySettlementPrice);

/// <summary>An option's terms: call or put, its underlying as IME names it, and its strike in Rial.</summary>
public sealed record ImeOptionTerms(bool IsCall, string Underlying, decimal StrikePrice);
