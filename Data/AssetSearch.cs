using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Data;

/// <summary>
/// Searching assets by symbol, identifier or name, shared by the public search endpoint and the public price pages.
/// </summary>
public static class AssetSearch
{
    // Each search word adds a LIKE over three columns, so only the first few count.
    private const int MaxWords = 10;

    /// <summary>
    /// The assets matching <paramref name="q"/>, best match first: the exact symbol, then symbols starting with it, then
    /// the rest by symbol. Null when <paramref name="q"/> has no words.
    /// </summary>
    public static IQueryable<Asset>? Apply(IQueryable<Asset> assets, string q)
    {
        // Stored symbols and names use Persian ی/ک, so a query typed with Arabic ي/ك (or pasted from TSETMC) still matches.
        var term = q.Trim().Replace('ي', 'ی').Replace('ك', 'ک');
        var prefix = $"{EscapeLike(term)}%";
        var symbol = term.ToUpperInvariant();

        // Every word has to appear somewhere in the symbol, identifier or name, in any order, so «تضمین مفید» finds
        // «تضمین اصل سرمایه مفید». Words are also split at a ZWNJ, so «سرمایه‌گذاری» and «سرمایه گذاری» find each other.
        var words = term.Split([' ', '\t', '‌'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .Take(MaxWords)
            .ToList();
        if (words.Count == 0)
            return null;

        foreach (var word in words)
        {
            var pattern = $"%{EscapeLike(word)}%";
            assets = assets.Where(a => EF.Functions.Like(a.Symbol, pattern, @"\")
                || EF.Functions.Like(a.Identifier, pattern, @"\")
                || EF.Functions.Like(a.Name, pattern, @"\"));
        }

        return assets
            .OrderByDescending(a => a.Symbol == symbol)
            .ThenByDescending(a => EF.Functions.Like(a.Symbol, prefix, @"\"))
            .ThenBy(a => a.Symbol);
    }

    // Escapes LIKE wildcards so user input is matched literally.
    private static string EscapeLike(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
