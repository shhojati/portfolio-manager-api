using System.Globalization;
using System.Text.Json.Serialization;

namespace PortfolioManager.Api.Tsetmc;

/// <summary>
/// Typed client for the JSON API behind tsetmc.com (cdn.tsetmc.com). The API is free but
/// unofficial and undocumented, so field names are TSETMC's own abbreviations.
/// </summary>
public sealed class TsetmcClient(HttpClient http)
{
    // Every paper type, without the order book; filtering to stocks/ETFs happens in the sync service.
    private const string MarketWatchUrl = "api/ClosingPrice/GetMarketWatch?market=0"
        + "&paperTypes%5B0%5D=1&paperTypes%5B1%5D=2&paperTypes%5B2%5D=3&paperTypes%5B3%5D=4&paperTypes%5B4%5D=5"
        + "&paperTypes%5B5%5D=6&paperTypes%5B6%5D=7&paperTypes%5B7%5D=8&paperTypes%5B8%5D=9"
        + "&showTraded=false&withBestLimits=false";

    /// <summary>Current prices of every listed instrument, in a single request.</summary>
    public async Task<IReadOnlyList<MarketWatchItem>> GetMarketWatchAsync(CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<MarketWatchResponse>(MarketWatchUrl, ct);
        return response?.MarketWatch ?? [];
    }

    /// <summary>Date of the latest trading session (market watch items carry no date of their own).</summary>
    public async Task<DateOnly?> GetLastTradingDateAsync(CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<MarketOverviewResponse>("api/MarketData/GetMarketOverview/1", ct);
        return ParseDate(response?.MarketOverview?.MarketActivityDEven ?? 0);
    }

    /// <summary>
    /// Codes of every instrument TSETMC keeps market watch filter statistics for, including suspended ones and ones
    /// that haven't traded for months, which the market watch leaves out. It is the only list found that has them, but
    /// it carries no names or ISINs, and it isn't a superset of the market watch either. A single large request
    /// (about 330,000 rows); the statistics themselves are ignored.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetAllInsCodesAsync(CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<InstValuesResponse>("api/MarketData/GetInstValueAllInstAllParam", ct);
        return (response?.InstValueAllInstAllParam ?? []).Select(v => v.InsCode).OfType<string>().ToHashSet();
    }

    /// <summary>
    /// An instrument's ISIN, symbol and name, in the shape of a market watch item with no price (see
    /// <see cref="GetClosingPriceAsync"/>), or null if TSETMC doesn't know the code.
    /// </summary>
    public async Task<MarketWatchItem?> GetInstrumentInfoAsync(string insCode, CancellationToken ct)
    {
        var info = (await http.GetFromJsonAsync<InstrumentInfoResponse>(
            $"api/Instrument/GetInstrumentInfo/{Uri.EscapeDataString(insCode)}", ct))?.InstrumentInfo;
        return string.IsNullOrEmpty(info?.InstrumentId)
            ? null
            : new MarketWatchItem(insCode, info.InstrumentId, info.Symbol, info.Name, 0);
    }

    /// <summary>An instrument's latest closing price and the day it was last traded, or null if it has none.</summary>
    public async Task<ClosingPrice?> GetClosingPriceAsync(string insCode, CancellationToken ct)
    {
        var info = (await http.GetFromJsonAsync<ClosingPriceInfoResponse>(
            $"api/ClosingPrice/GetClosingPriceInfo/{Uri.EscapeDataString(insCode)}", ct))?.ClosingPriceInfo;
        if (info is null || info.ClosingPrice <= 0 || ParseDate(info.LastTradeDEven) is not { } date)
            return null;

        return new ClosingPrice(date, info.ClosingPrice);
    }

    /// <summary>Latest redemption NAV (NAV ابطال) of an ETF, or null if TSETMC has none.</summary>
    public async Task<EtfNav?> GetEtfNavAsync(string insCode, CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<EtfResponse>($"api/Fund/GetETFByInsCode/{Uri.EscapeDataString(insCode)}", ct);
        var etf = response?.Etf;
        if (etf is null || etf.RedemptionPrice <= 0 || ParseDate(etf.DEven) is not { } date)
            return null;

        return new EtfNav(date, etf.RedemptionPrice);
    }

    // TSETMC dates are Gregorian yyyyMMdd integers.
    private static DateOnly? ParseDate(int dEven) =>
        DateOnly.TryParseExact(dEven.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", out var date) ? date : null;

    private sealed record MarketWatchResponse(List<MarketWatchItem>? MarketWatch);

    private sealed record MarketOverviewResponse(MarketOverview? MarketOverview);

    private sealed record MarketOverview(int MarketActivityDEven);

    private sealed record InstrumentInfoResponse(InstrumentInfo? InstrumentInfo);

    private sealed record InstrumentInfo(
        [property: JsonPropertyName("instrumentID")] string? InstrumentId,
        [property: JsonPropertyName("lVal18AFC")] string? Symbol,
        [property: JsonPropertyName("lVal30")] string? Name);

    private sealed record ClosingPriceInfoResponse(ClosingPriceInfo? ClosingPriceInfo);

    private sealed record ClosingPriceInfo(
        [property: JsonPropertyName("finalLastDate")] int LastTradeDEven,
        [property: JsonPropertyName("pClosing")] decimal ClosingPrice);

    private sealed record InstValuesResponse(List<InstValue>? InstValueAllInstAllParam);

    private sealed record InstValue([property: JsonPropertyName("insCode")] string? InsCode);

    private sealed record EtfResponse(EtfInfo? Etf);

    private sealed record EtfInfo(
        [property: JsonPropertyName("deven")] int DEven,
        [property: JsonPropertyName("pRedTran")] decimal RedemptionPrice);
}

public sealed record MarketWatchItem(
    [property: JsonPropertyName("insCode")] string? InsCode,
    [property: JsonPropertyName("insID")] string? Isin,
    [property: JsonPropertyName("lva")] string? Symbol,
    [property: JsonPropertyName("lvc")] string? Name,
    [property: JsonPropertyName("pcl")] decimal ClosingPrice);

public sealed record EtfNav(DateOnly Date, decimal Value);

public sealed record ClosingPrice(DateOnly LastTradeDate, decimal Value);
