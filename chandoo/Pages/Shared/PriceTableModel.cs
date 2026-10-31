using Chandoo.Api;
using Chandoo.Site;

namespace Chandoo.Pages.Shared;

/// <param name="IsPrice">Whether the values are prices in Rial (index values are points).</param>
/// <param name="ShowType">Adds a column naming each row's type, for lists that mix categories (search results).</param>
public sealed record PriceTableModel(IReadOnlyList<PriceRow> Rows, bool IsPrice = true, bool ShowType = false)
{
    public static string TypeName(string type) => type switch
    {
        AssetTypes.Option => "اختیار معامله",
        AssetTypes.Future => "آتی",
        AssetTypes.Etf => "صندوق بورسی",
        AssetTypes.Certificate => "گواهی سپرده",
        AssetTypes.Salaf => "سلف",
        AssetTypes.Index => "شاخص",
        _ => PriceCategory.All.FirstOrDefault(c => c.Types.Contains(type))?.Title ?? type,
    };
}
