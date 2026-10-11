using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PortfolioManager.Api.Tgju;

/// <summary>
/// Client for the live-price feed behind tgju.org (callN.tgju.org/ajax.json). No API key is needed; it holds the
/// latest free-market price of every instrument tgju tracks, keyed like "price_eur", with prices in Rial.
/// Each instrument's daily history comes from the site's own API (api.tgju.org), keyed the same way.
/// </summary>
public sealed partial class TgjuClient(HttpClient http, ILogger<TgjuClient> logger)
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

    /// <summary>Where tgju's site API serves an instrument's daily history (the table on its tgju.org/profile/{key}/history page).</summary>
    public static Uri HistoryUrl(string key) =>
        new($"https://api.tgju.org/v1/market/indicator/summary-table-data/{Uri.EscapeDataString(key)}");

    /// <summary>An instrument's closing price on every day tgju has one, oldest first, in Rial like the live feed.</summary>
    public async Task<IReadOnlyList<TgjuDayPrice>> GetDailyHistoryAsync(string key, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync(HistoryUrl(key), ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseHistory(doc.RootElement);
    }

    /// <summary>
    /// The days of a history response, oldest first. It is a DataTables page whose rows are arrays of display strings,
    /// some wrapped in HTML: open, low, high, close, change, change percent, Gregorian date ("2026/10/10") and Jalali
    /// date. Rows that don't parse are left out.
    /// </summary>
    public static IReadOnlyList<TgjuDayPrice> ParseHistory(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var days = new Dictionary<DateOnly, decimal>();
        foreach (var row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 4)
                continue;

            var cells = row.EnumerateArray().Select(CellText).ToList();
            // The Gregorian date is found by its form rather than its column; the Jalali one's year is below 1900.
            DateOnly? date = null;
            foreach (var cell in cells)
            {
                if (DateOnly.TryParseExact(cell, "yyyy/M/d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year >= 1900)
                {
                    date = d;
                    break;
                }
            }

            if (date is { } day &&
                decimal.TryParse(cells[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var close) && close > 0)
                days[day] = close;
        }

        return days.OrderBy(d => d.Key).Select(d => new TgjuDayPrice(d.Key, d.Value)).ToList();
    }

    // A cell's text without HTML tags, with Persian or Arabic digits and separators made Latin.
    private static string CellText(JsonElement cell)
    {
        var text = cell.ValueKind switch
        {
            JsonValueKind.String => cell.GetString() ?? "",
            JsonValueKind.Number => cell.GetRawText(),
            _ => "",
        };
        var chars = HtmlTag().Replace(text, "").Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '۰' and <= '۹' and var c => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' and var c => (char)('0' + (c - '٠')),
                '٬' => ',',
                var c => c,
            };
        }

        return new string(chars);
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTag();
}

/// <summary>An instrument's latest price in Rial, and when it was last updated (Tehran time).</summary>
public sealed record TgjuQuote(decimal Price, DateTime TehranTime);

/// <summary>An instrument's closing price on a (Tehran) day, in Rial.</summary>
public sealed record TgjuDayPrice(DateOnly Date, decimal Close);
