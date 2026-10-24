namespace PortfolioManager.Api.Ime;

/// <summary>
/// What a commodity exchange option is on. <see cref="CodePrefix"/> is the first two letters of its contract codes
/// (GBBA05C2200 is on شمش طلا), <see cref="Name"/> the underlying as IME's descriptions name it.
/// <see cref="UnderlyingSymbol"/> is the symbol of the asset it is on, when that asset is imported (a TSETMC fund or an
/// IME certificate). <see cref="ContractSize"/> is how many units of it one contract is for; IME's report doesn't say,
/// so it is null until it is known, and those options aren't imported.
/// </summary>
public sealed record ImeOptionUnderlying(string CodePrefix, string Name, string? UnderlyingSymbol, int? ContractSize);

/// <summary>The underlyings of the commodity exchange's options, as of 1405/07.</summary>
public static class ImeOptionUnderlyings
{
    public static readonly IReadOnlyList<ImeOptionUnderlying> All =
    [
        // 100 sut (0.1 g) of gold bar, one گواهی شمش طلا certificate, per contract.
        new("GB", "شمش طلا", "گواهی شمش طلا", 1),
        new("SL", "شمش نقره", "گواهی شمش نقره", null),
        new("CU", "مس کاتد", null, null),
        new("TL", "واحدهای سرمایه گذاری صندوق طلای پارسیان", "طلا", null),
        new("DG", "واحدهای سرمایه گذاری صندوق طلای درخشان آبان", "درخشان", null),
        new("ZR", "واحدهای سرمایه گذاری صندوق طلای کارآمد", "زرگر", null),
        new("KA", "واحدهای سرمایه گذاری صندوق طلای کهربا", "کهربا", null),
        // 1,000 fund units per contract.
        new("NQ", "واحدهای سرمایه گذاری صندوق نقره کهکشان فیروزه", "نقرابی", 1000),
    ];

    public static ImeOptionUnderlying? ByCode(string contractCode) =>
        contractCode.Length < 2 ? null : All.FirstOrDefault(u => contractCode.StartsWith(u.CodePrefix, StringComparison.Ordinal));
}
