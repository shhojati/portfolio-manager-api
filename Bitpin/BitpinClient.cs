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

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    /// <summary>
    /// A market's closing price on every day since <paramref name="since"/> (or since Bitpin listed it, if later),
    /// oldest first, from its daily candles; <paramref name="symbol"/> is e.g. "BTC_IRT". Bitpin's days start at
    /// midnight Tehran time. The last one is today, still open. Empty for a market Bitpin doesn't know.
    /// </summary>
    public async Task<IReadOnlyList<DayClose>> GetDailyClosesAsync(string symbol, DateOnly since, CancellationToken ct)
    {
        var from = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var to = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bars = await http.GetFromJsonAsync<List<Bar>>(
            $"v1/mkt/tv/get_bars/?symbol={Uri.EscapeDataString(symbol)}&res=1D&from={from}&to={to}", ct) ?? [];
        return bars
            .Where(b => b.Close > 0)
            // A bar's ts is when its day starts.
            .Select(b => new DayClose(
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds((long)b.Ts).UtcDateTime, Tehran)),
                b.Close!.Value))
            .DistinctBy(d => d.Date)
            .OrderBy(d => d.Date)
            .ToList();
    }

    private sealed record Bar(
        [property: JsonPropertyName("ts")] double Ts,
        [property: JsonPropertyName("close"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? Close);

    private sealed record MarketsPage(
        [property: JsonPropertyName("next")] string? Next,
        [property: JsonPropertyName("results")] List<Market>? Results);
}

/// <summary>A market's closing price on a (Tehran) day, in the market's quote currency.</summary>
public sealed record DayClose(DateOnly Date, decimal Close);

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
