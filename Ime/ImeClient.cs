using System.Text.Json.Serialization;

namespace PortfolioManager.Api.Ime;

/// <summary>
/// Typed client for the JSON API behind the commodity exchange's gold and silver sites (gold.ime.co.ir and
/// silver.ime.co.ir), served from dataapi.ime.co.ir. The API is free but unofficial and undocumented; it is what those
/// sites' daily trade reports call. Prices are in Rial per certificate. IME's servers seem to answer only Iranian IPs.
/// </summary>
public sealed class ImeClient(HttpClient http)
{
    // The market id of continuous deposit certificates (گواهی سپرده پیوسته) on the sites' requests.
    private const int CertificateMarketId = 22;

    private const int PageSize = 100;

    /// <summary>
    /// A continuous deposit certificate's daily trade summaries between two days (inclusive). <paramref name="site"/> is
    /// the site whose reports the contract is on, sent as the request's origin, as the site itself does.
    /// </summary>
    public async Task<IReadOnlyList<CertificateTrade>> GetCertificateTradesAsync(
        string contractCode, Uri site, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var trades = new List<CertificateTrade>();
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/CDC/CDCTrades")
            {
                Content = JsonContent.Create(new
                {
                    fromDate = from.ToString("yyyy-MM-dd"),
                    toDate = to.ToString("yyyy-MM-dd"),
                    pageNumber = page,
                    pageSize = PageSize,
                    marketId = CertificateMarketId,
                    customFilter = contractCode,
                }),
            };
            request.Headers.Add("Origin", site.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = site;

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<TradesPage>(ct);
            trades.AddRange(result?.Data ?? []);

            // A page limit too, in case a change to the API makes it report a next page forever.
            if (result?.HasNextPage != true || page >= 100)
                return trades;
        }
    }

    private sealed record TradesPage(List<CertificateTrade>? Data, bool HasNextPage);
}

/// <summary>
/// A contract's trading on one day. <see cref="Date"/> is the trading day. <see cref="TodaySettlementPrice"/> is the
/// day's settlement price, the exchange's official price for the day; <see cref="LastPrice"/> the last trade's.
/// </summary>
public sealed record CertificateTrade(
    [property: JsonPropertyName("ContractCode")] string? ContractCode,
    [property: JsonPropertyName("ContractDescription")] string? ContractDescription,
    [property: JsonPropertyName("DT")] DateTime? Date,
    [property: JsonPropertyName("LastPrice")] decimal? LastPrice,
    [property: JsonPropertyName("TodaySettlementPrice")] decimal? TodaySettlementPrice,
    [property: JsonPropertyName("TradesVolume")] decimal? TradesVolume);
