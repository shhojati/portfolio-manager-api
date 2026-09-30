using System.Globalization;
using System.Text.Json;

namespace PortfolioManager.Api.Tgju;

/// <summary>
/// Client for the live-price feed behind tgju.org (callN.tgju.org/ajax.json). No API key is needed; it holds the
/// latest free-market price of every instrument tgju tracks, keyed like "price_eur", with prices in Rial.
/// </summary>
public sealed class TgjuClient(HttpClient http, ILogger<TgjuClient> logger)
{
    // tgju serves the same feed from several hosts, and any of them can hang for a while, so they are tried in turn.
    private static readonly string[] Mirrors = ["call1", "call2", "call3", "call4"];
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(20);

    // The mirror that answered last time, tried first on the next call.
    private static int _preferred;

    /// <summary>Latest quote of every instrument, keyed by tgju's name for it. Entries that can't be parsed are left out.</summary>
    public async Task<IReadOnlyDictionary<string, TgjuQuote>> GetQuotesAsync(CancellationToken ct)
    {
        var first = Volatile.Read(ref _preferred);
        for (var i = 0; ; i++)
        {
            var mirror = (first + i) % Mirrors.Length;
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(AttemptTimeout);
            try
            {
                var quotes = await GetQuotesAsync(new Uri($"https://{Mirrors[mirror]}.tgju.org/ajax.json"), attempt.Token);
                Volatile.Write(ref _preferred, mirror);
                return quotes;
            }
            catch (Exception ex) when (i < Mirrors.Length - 1 && !ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                logger.LogWarning("tgju mirror {Mirror} failed ({Error}), trying the next one", Mirrors[mirror], ex.Message);
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, TgjuQuote>> GetQuotesAsync(Uri url, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var quotes = new Dictionary<string, TgjuQuote>(StringComparer.Ordinal);
        if (!doc.RootElement.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object)
            return quotes;

        // Parsed one by one so a single odd entry (tgju mixes in indexes, crypto, gold...) doesn't fail the whole feed.
        foreach (var item in current.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.Object &&
                TryParsePrice(item.Value, out var price) &&
                TryParseTime(item.Value, out var time))
            {
                quotes[item.Name] = new TgjuQuote(price, time);
            }
        }

        return quotes;
    }

    // "p" is a string with thousands separators, e.g. "2,563,000".
    private static bool TryParsePrice(JsonElement quote, out decimal price)
    {
        price = 0;
        if (!quote.TryGetProperty("p", out var p)) return false;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDecimal(out price),
            JsonValueKind.String => decimal.TryParse(p.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out price),
            _ => false,
        };
    }

    // "ts" is Tehran local time, e.g. "2026-09-30 13:56:51".
    private static bool TryParseTime(JsonElement quote, out DateTime time)
    {
        time = default;
        return quote.TryGetProperty("ts", out var ts) && ts.ValueKind == JsonValueKind.String &&
               DateTime.TryParseExact(ts.GetString(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}

/// <summary>An instrument's latest price in Rial, and when it was last updated (Tehran time).</summary>
public sealed record TgjuQuote(decimal Price, DateTime TehranTime);
