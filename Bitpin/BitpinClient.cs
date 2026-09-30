using System.Text.Json.Serialization;

namespace PortfolioManager.Api.Bitpin;

/// <summary>
/// Typed client for Bitpin's public market API (api.bitpin.ir). No API key is needed; prices of
/// Toman (IRT) markets are in Toman.
/// </summary>
public sealed class BitpinClient(HttpClient http)
{
    /// <summary>Latest price of every market (IRT and USDT pairs), in a single request.</summary>
    public async Task<IReadOnlyList<Ticker>> GetTickersAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<Ticker>>("api/v1/mkt/tickers/", ct) ?? [];

    /// <summary>Every market with its currencies' names. Much larger than the tickers, so only fetched when new coins appear.</summary>
    public async Task<IReadOnlyList<Market>> GetMarketsAsync(CancellationToken ct)
    {
        var markets = new List<Market>();
        string? url = "v1/mkt/markets/";
        while (url is not null)
        {
            var page = await http.GetFromJsonAsync<MarketsPage>(url, ct);
            markets.AddRange(page?.Results ?? []);
            url = page?.Next;
        }

        return markets;
    }

    private sealed record MarketsPage(
        [property: JsonPropertyName("next")] string? Next,
        [property: JsonPropertyName("results")] List<Market>? Results);
}

/// <summary>A market's latest price; <see cref="Symbol"/> is e.g. "BTC_IRT".</summary>
public sealed record Ticker(
    [property: JsonPropertyName("symbol")] string? Symbol,
    [property: JsonPropertyName("price")] decimal? Price);

/// <summary>A market pairing <see cref="Base"/> (the coin) with <see cref="Quote"/> (IRT or USDT).</summary>
public sealed record Market(
    [property: JsonPropertyName("currency1")] Currency? Base,
    [property: JsonPropertyName("currency2")] Currency? Quote);

public sealed record Currency(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("title_fa")] string? TitleFa);
