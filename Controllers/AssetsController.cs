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

    [HttpGet]
    public async Task<ActionResult<List<AssetDto>>> GetAll()
    {
        return await db.Assets
            .OrderBy(a => a.Symbol)
            .Select(a => ToDto(a))
            .ToListAsync();
    }

    /// <summary>Search assets by symbol, identifier or name. Public: no sign-in needed, rate limited per IP.</summary>
    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<List<AssetDto>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Search query 'q' is required.");

        var term = q.Trim();
        // escape LIKE wildcards so user input is matched literally
        var escaped = term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
        var pattern = $"%{escaped}%";
        var prefix = $"{escaped}%";
        var symbol = term.ToUpperInvariant();

        return await db.Assets
            .Where(a => EF.Functions.Like(a.Symbol, pattern, @"\")
                || EF.Functions.Like(a.Identifier, pattern, @"\")
                || EF.Functions.Like(a.Name, pattern, @"\"))
            .OrderByDescending(a => a.Symbol == symbol)
            .ThenByDescending(a => EF.Functions.Like(a.Symbol, prefix, @"\"))
            .ThenBy(a => a.Symbol)
            .Select(a => ToDto(a))
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
            .Select(a => ToDto(a))
            .ToListAsync();

        return assets.OrderBy(a => ids.IndexOf(a.Identifier.ToUpperInvariant())).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AssetDto>> GetById(int id)
    {
        var asset = await db.Assets.FindAsync(id);
        return asset is null ? NotFound() : ToDto(asset);
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

        return CreatedAtAction(nameof(GetById), new { id = asset.Id }, ToDto(asset));
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

    private static AssetDto ToDto(Asset a) => new(a.Id, a.Symbol, a.Identifier, a.Name, a.Type, a.CreatedAt);
}
