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
public class PricesController(AppDbContext db) : ControllerBase
{
    // Caps the work one (possibly anonymous) request can cause.
    private const int MaxIdentifiers = 100;

    /// <summary>Get all prices, optionally filtered by asset.</summary>
    [HttpGet]
    public async Task<ActionResult<List<PriceDto>>> GetAll([FromQuery] int? assetId)
    {
        var query = db.Prices.AsQueryable();
        if (assetId is not null)
            query = query.Where(p => p.AssetId == assetId);

        return await query
            .OrderByDescending(p => p.Date)
            .Select(p => ToDto(p))
            .ToListAsync();
    }

    /// <summary>Get the latest price of each asset in the given list of asset identifiers.</summary>
    /// <remarks>
    /// Identifiers with no matching asset or no prices are omitted from the result.
    /// Public: no sign-in needed, rate limited per IP; at most 100 identifiers per request.
    /// </remarks>
    [HttpGet("latest")]
    [AllowAnonymous]
    public async Task<ActionResult<List<AssetPriceDto>>> GetLatestByIdentifiers([FromQuery] List<string> identifiers)
    {
        var ids = identifiers.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest("At least one identifier is required.");
        if (ids.Count > MaxIdentifiers)
            return BadRequest($"At most {MaxIdentifiers} identifiers per request.");

        var prices = await db.Prices
            .Where(p => ids.Contains(p.Asset!.Identifier)
                && p.Date == db.Prices.Where(q => q.AssetId == p.AssetId).Max(q => q.Date))
            .Select(p => new AssetPriceDto(p.Asset!.Identifier, p.AssetId, p.Id, p.Value, p.Date, p.Nav))
            .ToListAsync();

        // Several prices can share the latest date; keep the most recently inserted one per asset.
        return prices
            .GroupBy(p => p.AssetId)
            .Select(g => g.MaxBy(p => p.PriceId)!)
            .OrderBy(p => ids.IndexOf(p.Identifier))
            .ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PriceDto>> GetById(int id)
    {
        var price = await db.Prices.FindAsync(id);
        return price is null ? NotFound() : ToDto(price);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<PriceDto>> Create(PriceRequest request)
    {
        if (!await db.Assets.AnyAsync(a => a.Id == request.AssetId))
            return BadRequest($"Asset {request.AssetId} does not exist.");

        var price = new Price { AssetId = request.AssetId, Value = request.Value, Date = request.Date, Nav = request.Nav };
        db.Prices.Add(price);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = price.Id }, ToDto(price));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, PriceRequest request)
    {
        var price = await db.Prices.FindAsync(id);
        if (price is null)
            return NotFound();

        if (!await db.Assets.AnyAsync(a => a.Id == request.AssetId))
            return BadRequest($"Asset {request.AssetId} does not exist.");

        price.AssetId = request.AssetId;
        price.Value = request.Value;
        price.Date = request.Date;
        price.Nav = request.Nav;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var price = await db.Prices.FindAsync(id);
        if (price is null)
            return NotFound();

        db.Prices.Remove(price);
        await db.SaveChangesAsync();

        return NoContent();
    }

    private static PriceDto ToDto(Price p) => new(p.Id, p.AssetId, p.Value, p.Date, p.Nav);
}
