using PortfolioManager.Api.Public;
using PortfolioManager.Api.Tsetmc;

namespace PortfolioManager.Api.Pages.Shared;

/// <param name="IsPrice">Whether the values are prices in Rial (index values are points).</param>
/// <param name="ShowType">Adds a column naming each row's type, for lists that mix categories (search results).</param>
public sealed record PriceTableModel(IReadOnlyList<PriceRow> Rows, bool IsPrice = true, bool ShowType = false)
{
    public static string TypeName(string type) => type switch
    {
        TsetmcDerivativesSyncService.OptionType => "اختیار معامله",
        TsetmcDerivativesSyncService.FutureType => "آتی",
        TsetmcPriceSyncService.EtfType => "صندوق بورسی",
        TsetmcPriceSyncService.CertificateType => "گواهی سپرده",
        TsetmcPriceSyncService.SalafType => "سلف",
        TsetmcIndices.IndexType => "شاخص",
        _ => PriceCategory.All.FirstOrDefault(c => c.Types.Contains(type))?.Title ?? type,
    };
}
