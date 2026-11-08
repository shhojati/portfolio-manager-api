using System.Globalization;
using System.Text;
using PortfolioManager.Api.Dtos;

namespace PortfolioManager.Api.Bale;

public static class BalePriceMessages
{
    public static IReadOnlyList<string> Create(IReadOnlyList<BoardItemDto> prices, DateTimeOffset slot)
    {
        if (prices.Count == 0) return [];
        var culture = CultureInfo.GetCultureInfo("fa-IR");
        var header = $"📊 آخرین قیمت‌های ثبت‌شده\nزمان انتشار: {slot.DateTime.ToString("yyyy/MM/dd HH:mm", culture)} (تهران)\nمبالغ به ریال\n\n";
        var result = new List<string>();
        var message = new StringBuilder(header);
        foreach (var item in prices)
        {
            var unit = string.IsNullOrWhiteSpace(item.Unit) ? "" : $" / {item.Unit}";
            var line = $"{item.Symbol}: {item.Value.ToString("#,0.######", CultureInfo.InvariantCulture)}{unit}\n" +
                $"تاریخ قیمت: {item.Date.ToString("yyyy/MM/dd", culture)}\n\n";
            if (message.Length + line.Length > 4096)
            {
                result.Add(message.ToString());
                message = new StringBuilder(header);
            }
            message.Append(line);
        }
        result.Add(message.ToString());
        return result;
    }
}
