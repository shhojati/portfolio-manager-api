using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Auth;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.Models;
using PortfolioManager.Api.RateLimiting;

namespace PortfolioManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Readers)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public class AssetsController(AppDbContext db) : ControllerBase
{
    // Caps the work one (possibly anonymous) request can cause.
    private const int MaxIdentifiers = 100;

    // Each search word adds a LIKE over three columns, so only the first few count.
    private const int MaxSearchWords = 10;

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    /// <summary>All assets, optionally only one type, or the options and futures on one underlying asset.</summary>
    /// <param name="type">Only assets of this type, e.g. Stock or Option.</param>
    /// <param name="underlyingAssetId">Only the options and futures on this asset (its option chain).</param>
    /// <param name="excludeExpired">Leave out options and futures past their expiry date (Tehran time).</param>
    [HttpGet]
    public async Task<ActionResult<List<AssetDto>>> GetAll(
        [FromQuery] string? type, [FromQuery] int? underlyingAssetId, [FromQuery] bool excludeExpired = false)
    {
        var query = db.Assets.AsQueryable();
        if (!string.IsNullOrWhiteSpace(type))
        {
            var wanted = type.Trim();
            query = query.Where(a => a.Type == wanted);
        }
        if (underlyingAssetId is not null)
            query = query.Where(a => a.Contract != null && a.Contract.UnderlyingAssetId == underlyingAssetId);
        if (excludeExpired)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran));
            query = query.Where(a => a.Contract == null || a.Contract.ExpiryDate >= today);
        }

        return await query
            .OrderBy(a => a.Symbol)
            .Select(AssetDtoMapping.Projection)
            .ToListAsync();
    }

    /// <summary>Search assets by symbol, identifier or name. Public: no sign-in needed, rate limited per IP.</summary>
    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<List<AssetDto>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Search query 'q' is required.");

        // Stored symbols and names use Persian ی/ک, so a query typed with Arabic ي/ك (or pasted from TSETMC) still matches.
        var term = q.Trim().Replace('ي', 'ی').Replace('ك', 'ک');
        var prefix = $"{EscapeLike(term)}%";
        var symbol = term.ToUpperInvariant();

        // Every word has to appear somewhere in the symbol, identifier or name, in any order, so «تضمین مفید» finds
        // «تضمین اصل سرمایه مفید». Words are also split at a ZWNJ, so «سرمایه‌گذاری» and «سرمایه گذاری» find each other.
        var words = term.Split([' ', '\t', '\u200c'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .Take(MaxSearchWords)
            .ToList();
        if (words.Count == 0)
            return BadRequest("Search query 'q' is required.");

        var query = db.Assets.AsQueryable();
        foreach (var word in words)
        {
            var pattern = $"%{EscapeLike(word)}%";
            query = query.Where(a => EF.Functions.Like(a.Symbol, pattern, @"\")
                || EF.Functions.Like(a.Identifier, pattern, @"\")
                || EF.Functions.Like(a.Name, pattern, @"\"));
        }

        return await query
            .OrderByDescending(a => a.Symbol == symbol)
            .ThenByDescending(a => EF.Functions.Like(a.Symbol, prefix, @"\"))
            .ThenBy(a => a.Symbol)
            .Select(AssetDtoMapping.Projection)
            .Take(50)
            .ToListAsync();
    }

    /// <summary>Get the assets with the given identifiers, matched case-insensitively.</summary>
    /// <remarks>
    /// Identifiers with no matching asset are omitted from the result.
    /// Public: no sign-in needed, rate limited per IP; at most 100 identifiers per request.
    /// </remarks>
    [HttpGet("by-identifiers")]
    [AllowAnonymous]
    public async Task<ActionResult<List<AssetDto>>> GetByIdentifiers([FromQuery] List<string> identifiers)
    {
        var ids = identifiers.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim().ToUpperInvariant()).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest("At least one identifier is required.");
        if (ids.Count > MaxIdentifiers)
            return BadRequest($"At most {MaxIdentifiers} identifiers per request.");

        var assets = await db.Assets
            .Where(a => ids.Contains(a.Identifier.ToUpper()))
            .Select(AssetDtoMapping.Projection)
            .ToListAsync();

        return assets.OrderBy(a => ids.IndexOf(a.Identifier.ToUpperInvariant())).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AssetDto>> GetById(int id)
    {
        var asset = await db.Assets.Where(a => a.Id == id).Select(AssetDtoMapping.Projection).FirstOrDefaultAsync();
        return asset is null ? NotFound() : asset;
    }

    /// <summary>Price history of an asset, newest first. Public: no sign-in needed, rate limited per IP.</summary>
    [HttpGet("{id:int}/prices")]
    [AllowAnonymous]
    public async Task<ActionResult<List<PriceDto>>> GetPrices(int id)
    {
        if (!await db.Assets.AnyAsync(a => a.Id == id))
            return NotFound();

        return await db.Prices
            .Where(p => p.AssetId == id)
            .OrderByDescending(p => p.Date)
            .Select(p => new PriceDto(p.Id, p.AssetId, p.Value, p.Date, p.Nav))
            .ToListAsync();
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<AssetDto>> Create(AssetRequest request)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(a => a.Symbol == symbol))
            return Conflict($"Asset with symbol '{symbol}' already exists.");

        var identifier = request.Identifier.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(a => a.Identifier == identifier))
            return Conflict($"Asset with identifier '{identifier}' already exists.");

        var asset = new Asset { Symbol = symbol, Identifier = identifier, Name = request.Name, Type = request.Type };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = asset.Id }, AssetDtoMapping.ToDto(asset));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, AssetRequest request)
    {
        var asset = await db.Assets.FindAsync(id);
        if (asset is null)
            return NotFound();

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(a => a.Symbol == symbol && a.Id != id))
            return Conflict($"Asset with symbol '{symbol}' already exists.");

        var identifier = request.Identifier.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(a => a.Identifier == identifier && a.Id != id))
            return Conflict($"Asset with identifier '{identifier}' already exists.");

        asset.Symbol = symbol;
        asset.Identifier = identifier;
        asset.Name = request.Name;
        asset.Type = request.Type;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var asset = await db.Assets.FindAsync(id);
        if (asset is null)
            return NotFound();

        db.Assets.Remove(asset); // prices are removed by cascade delete
        await db.SaveChangesAsync();

        return NoContent();
    }

    // Escapes LIKE wildcards so user input is matched literally.
    private static string EscapeLike(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
