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

    /// <summary>How many units of the underlying one contract of an option or future is for, or null if TSETMC doesn't say.</summary>
    public async Task<int?> GetContractSizeAsync(string insCode, CancellationToken ct)
    {
        var info = (await http.GetFromJsonAsync<InstrumentInfoResponse>(
            $"api/Instrument/GetInstrumentInfo/{Uri.EscapeDataString(insCode)}", ct))?.InstrumentInfo;
        return info?.ContractSize > 0 ? info.ContractSize : null;
    }

    /// <summary>
    /// Every open TSE and IFB option contract with its terms and current price, in a single request (about 1,700
    /// contracts). TSETMC lists them as call/put pairs with the same strike and expiry; each side is a quote of its own.
    /// It has no ISINs.
    /// </summary>
    public async Task<IReadOnlyList<OptionQuote>> GetOptionMarketWatchAsync(CancellationToken ct)
    {
        // The last segment is the market: 0 both, 1 TSE, 2 IFB.
        var response = await http.GetFromJsonAsync<OptionMarketWatchResponse>("api/Instrument/GetInstrumentOptionMarketWatch/0", ct);
        var quotes = new List<OptionQuote>();
        foreach (var pair in response?.InstrumentOptMarketWatch ?? [])
        {
            if (!DateOnly.TryParseExact(pair.EndDate, "yyyyMMdd", out var expiry))
                continue;

            quotes.Add(new OptionQuote(pair.CallInsCode, pair.CallSymbol, pair.CallName, true, pair.StrikePrice,
                pair.ContractSize, expiry, pair.UnderlyingInsCode, pair.UnderlyingSymbol, pair.CallClosingPrice));
            quotes.Add(new OptionQuote(pair.PutInsCode, pair.PutSymbol, pair.PutName, false, pair.StrikePrice,
                pair.ContractSize, expiry, pair.UnderlyingInsCode, pair.UnderlyingSymbol, pair.PutClosingPrice));
        }

        return quotes;
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

    /// <summary>
    /// An instrument's closing price on every day it traded since it was listed (one request, a few thousand rows for
    /// an old stock), oldest first. These are the prices of the day, not adjusted for later capital increases.
    /// </summary>
    public async Task<IReadOnlyList<ClosingPrice>> GetDailyClosingPricesAsync(string insCode, CancellationToken ct)
    {
        // The last segment is how many days back; 0 means all of them.
        var response = await http.GetFromJsonAsync<ClosingPriceDailyResponse>(
            $"api/ClosingPrice/GetClosingPriceDailyList/{Uri.EscapeDataString(insCode)}/0", ct);
        return (response?.ClosingPriceDaily ?? [])
            .Where(d => d.ClosingPrice > 0)
            .Select(d => ParseDate(d.DEven) is { } date ? new ClosingPrice(date, d.ClosingPrice) : null)
            .OfType<ClosingPrice>()
            .OrderBy(d => d.LastTradeDate)
            .ToList();
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

    /// <summary>
    /// A market index's value now (during a session) or at the last close, from the one request that has every index
    /// TSETMC publishes; null if it isn't among them. The value has no date of its own (see <see cref="GetLastTradingDateAsync"/>).
    /// </summary>
    public async Task<decimal?> GetIndexValueAsync(string insCode, CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<IndexLastAllResponse>("api/Index/GetIndexB1LastAll/All/1", ct);
        var index = response?.IndexB1?.FirstOrDefault(i => i.InsCode == insCode);
        return index?.Value > 0 ? index.Value : null;
    }

    /// <summary>A market index's closing value on every trading day since 1387 (about 4,300 rows), oldest first.</summary>
    public async Task<IReadOnlyList<ClosingPrice>> GetIndexHistoryAsync(string insCode, CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<IndexHistoryResponse>(
            $"api/Index/GetIndexB2History/{Uri.EscapeDataString(insCode)}", ct);
        return (response?.IndexB2 ?? [])
            .Where(d => d.Value > 0)
            .Select(d => ParseDate(d.DEven) is { } date ? new ClosingPrice(date, d.Value) : null)
            .OfType<ClosingPrice>()
            .OrderBy(d => d.LastTradeDate)
            .ToList();
    }

    // TSETMC dates are Gregorian yyyyMMdd integers.
    private static DateOnly? ParseDate(int dEven) =>
        DateOnly.TryParseExact(dEven.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", out var date) ? date : null;

    private sealed record MarketWatchResponse(List<MarketWatchItem>? MarketWatch);

    private sealed record ClosingPriceDailyResponse(List<ClosingPriceDaily>? ClosingPriceDaily);

    private sealed record ClosingPriceDaily(
        [property: JsonPropertyName("dEven")] int DEven,
        [property: JsonPropertyName("pClosing")] decimal ClosingPrice);

    private sealed record MarketOverviewResponse(MarketOverview? MarketOverview);

    private sealed record MarketOverview(int MarketActivityDEven);

    private sealed record InstrumentInfoResponse(InstrumentInfo? InstrumentInfo);

    private sealed record InstrumentInfo(
        [property: JsonPropertyName("instrumentID")] string? InstrumentId,
        [property: JsonPropertyName("lVal18AFC")] string? Symbol,
        [property: JsonPropertyName("lVal30")] string? Name,
        [property: JsonPropertyName("contractSize")] int ContractSize);

    private sealed record OptionMarketWatchResponse(List<OptionPair>? InstrumentOptMarketWatch);

    private sealed record OptionPair(
        [property: JsonPropertyName("insCode_C")] string? CallInsCode,
        [property: JsonPropertyName("lVal18AFC_C")] string? CallSymbol,
        [property: JsonPropertyName("lVal30_C")] string? CallName,
        [property: JsonPropertyName("pClosing_C")] decimal CallClosingPrice,
        [property: JsonPropertyName("insCode_P")] string? PutInsCode,
        [property: JsonPropertyName("lVal18AFC_P")] string? PutSymbol,
        [property: JsonPropertyName("lVal30_P")] string? PutName,
        [property: JsonPropertyName("pClosing_P")] decimal PutClosingPrice,
        [property: JsonPropertyName("strikePrice")] decimal StrikePrice,
        [property: JsonPropertyName("contractSize")] int ContractSize,
        [property: JsonPropertyName("endDate")] string? EndDate,
        [property: JsonPropertyName("uaInsCode")] string? UnderlyingInsCode,
        [property: JsonPropertyName("lval30_UA")] string? UnderlyingSymbol);

    private sealed record ClosingPriceInfoResponse(ClosingPriceInfo? ClosingPriceInfo);

    private sealed record ClosingPriceInfo(
        [property: JsonPropertyName("finalLastDate")] int LastTradeDEven,
        [property: JsonPropertyName("pClosing")] decimal ClosingPrice);

    private sealed record InstValuesResponse(List<InstValue>? InstValueAllInstAllParam);

    private sealed record InstValue([property: JsonPropertyName("insCode")] string? InsCode);

    private sealed record IndexLastAllResponse(List<IndexLast>? IndexB1);

    private sealed record IndexLast(
        [property: JsonPropertyName("insCode")] string? InsCode,
        [property: JsonPropertyName("xDrNivJIdx004")] decimal Value);

    private sealed record IndexHistoryResponse(List<IndexDay>? IndexB2);

    // The history's insCode is a JSON number too large for a double, so it isn't read.
    private sealed record IndexDay(
        [property: JsonPropertyName("dEven")] int DEven,
        [property: JsonPropertyName("xNivInuClMresIbs")] decimal Value);

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

/// <summary>One side of an option pair: a call or a put. Prices are Rial per unit of the underlying.</summary>
public sealed record OptionQuote(
    string? InsCode,
    string? Symbol,
    string? Name,
    bool IsCall,
    decimal StrikePrice,
    int ContractSize,
    DateOnly ExpiryDate,
    string? UnderlyingInsCode,
    string? UnderlyingSymbol,
    decimal ClosingPrice);

public sealed record EtfNav(DateOnly Date, decimal Value);

public sealed record ClosingPrice(DateOnly LastTradeDate, decimal Value);
