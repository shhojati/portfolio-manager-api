using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PortfolioManager.Api.Auth;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.RateLimiting;
using PortfolioManager.Api.Tgju;

namespace PortfolioManager.Api.Controllers;

/// <summary>Checks, run by hand, of what the server can reach from where it's hosted.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public partial class DiagnosticsController(IHttpClientFactory httpFactory, TgjuClient tgju) : ControllerBase
{
    private static readonly TimeSpan HistoryTimeout = TimeSpan.FromSeconds(30);
    private const int SampleRowCount = 3;
    private const int SampleRowLength = 600;

    /// <summary>Ask tgju's site API (api.tgju.org) for an instrument's daily history, and report how it went.</summary>
    /// <remarks>
    /// Past prices of gold, coins and currencies would come from there, a different host than the live feed the price
    /// sync uses (callN.tgju.org), and it may not be reachable from every network. <c>key</c> is tgju's name for the
    /// instrument, as in the live feed (default <c>mesghal</c>). Nothing is stored.
    /// </remarks>
    [HttpGet("tgju-history")]
    public async Task<ActionResult<TgjuHistoryCheckDto>> CheckTgjuHistory([FromQuery] string key = "mesghal", CancellationToken ct = default)
    {
        key = key.Trim();
        if (!TgjuKey().IsMatch(key))
            return BadRequest("key must be tgju's name for an instrument, e.g. mesghal or price_eur.");

        var url = TgjuClient.HistoryUrl(key);

        List<string> addresses = [];
        string? dnsError = null;
        try
        {
            addresses = (await Dns.GetHostAddressesAsync(url.Host, ct)).Select(a => a.ToString()).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            dnsError = Describe(ex);
        }

        int? statusCode = null;
        string? contentType = null, error = null;
        long? bytes = null;
        int? recordsTotal = null, rows = null, parsedDays = null;
        DayPriceDto? oldest = null, newest = null;
        List<string> sampleRows = [];

        // The same client settings (user agent, decompression) the price sync's TgjuClient has.
        var http = httpFactory.CreateClient(nameof(TgjuClient));
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(HistoryTimeout);
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(url, attempt.Token);
            statusCode = (int)response.StatusCode;
            contentType = response.Content.Headers.ContentType?.ToString();
            var body = await response.Content.ReadAsStringAsync(attempt.Token);
            bytes = body.Length;

            if (!response.IsSuccessStatusCode)
            {
                error = $"HTTP {statusCode}";
                sampleRows.Add(Truncate(body));
            }
            else
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("recordsTotal", out var total))
                    recordsTotal = total.ValueKind == JsonValueKind.Number ? total.GetInt32()
                        : int.TryParse(total.GetString(), out var n) ? n : null;
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    rows = data.GetArrayLength();
                    sampleRows = data.EnumerateArray().Take(SampleRowCount).Select(r => Truncate(r.GetRawText())).ToList();
                }
                else
                {
                    sampleRows.Add(Truncate(body));
                }

                var days = TgjuClient.ParseHistory(root);
                parsedDays = days.Count;
                if (days.Count > 0)
                {
                    oldest = new DayPriceDto(days[0].Date, days[0].Close);
                    newest = new DayPriceDto(days[^1].Date, days[^1].Close);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            error = $"No answer within {HistoryTimeout.TotalSeconds:0} seconds";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            error = Describe(ex);
        }

        var elapsed = watch.ElapsedMilliseconds;

        // The live feed's price of the same instrument, to check that the newest close is in the same unit.
        decimal? livePrice = null;
        DateTime? liveTime = null;
        string? liveError = null;
        try
        {
            var quotes = await tgju.GetQuotesAsync(ct);
            if (quotes.TryGetValue(key, out var quote))
                (livePrice, liveTime) = (quote.Price, quote.TehranTime);
            else
                liveError = $"The live feed has no '{key}'";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            liveError = Describe(ex);
        }

        return new TgjuHistoryCheckDto(
            key, url.ToString(), addresses, dnsError, statusCode, elapsed, contentType, bytes, error,
            recordsTotal, rows, parsedDays, oldest, newest, sampleRows, livePrice, liveTime, liveError);
    }

    // An exception's message with its inner ones, e.g. "... (Connection timed out)".
    private static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {e.Message}");
        return string.Join(" ← ", parts);
    }

    private static string Truncate(string text) =>
        text.Length <= SampleRowLength ? text : text[..SampleRowLength] + "…";

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex TgjuKey();
}
