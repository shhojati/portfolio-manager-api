using System.Text.Json.Serialization;

namespace PortfolioManager.Api.Fipiran;

/// <summary>
/// Typed client for the JSON API behind fipiran.ir (www.fipiran.ir/services). The API is free but unofficial and
/// undocumented; it is what the site's own fund pages call. NAVs are in Rial per unit.
/// </summary>
public sealed class FipiranClient(HttpClient http)
{
    /// <summary>Latest NAVs of every mutual fund registered with SEO (exchange-traded and issuance/redemption), in a single request.</summary>
    public async Task<IReadOnlyList<FundItem>> GetFundsAsync(CancellationToken ct)
    {
        // The site posts its compare-page filters here; an empty body means every fund, as of the latest NAV date.
        using var response = await http.PostAsJsonAsync("services/fund/fundcompare", new { }, ct);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<FundComparePage>(ct);
        return page?.Items ?? [];
    }

    private sealed record FundComparePage(List<FundItem>? Items);
}

/// <summary>
/// A fund's latest NAVs. <see cref="RegNo"/> is its SEO registration number. <see cref="TypeOfInvest"/> is
/// "Negotiable" for exchange-traded funds and "IssuanceAndCancellation" for funds bought from and redeemed with the fund itself.
/// </summary>
public sealed record FundItem(
    [property: JsonPropertyName("regNo")] string? RegNo,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("fundType")] int FundType,
    [property: JsonPropertyName("typeOfInvest")] string? TypeOfInvest,
    [property: JsonPropertyName("cancelNav")] decimal? CancelNav,
    [property: JsonPropertyName("issueNav")] decimal? IssueNav,
    [property: JsonPropertyName("date")] DateTime? Date);
