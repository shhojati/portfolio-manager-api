using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PricesController(AppDbContext db) : ControllerBase
{
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

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PriceDto>> GetById(int id)
    {
        var price = await db.Prices.FindAsync(id);
        return price is null ? NotFound() : ToDto(price);
    }

    [HttpPost]
    public async Task<ActionResult<PriceDto>> Create(PriceRequest request)
    {
        if (!await db.Assets.AnyAsync(a => a.Id == request.AssetId))
            return BadRequest($"Asset {request.AssetId} does not exist.");

        var price = new Price { AssetId = request.AssetId, Value = request.Value, Date = request.Date };
        db.Prices.Add(price);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = price.Id }, ToDto(price));
    }

    [HttpPut("{id:int}")]
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
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var price = await db.Prices.FindAsync(id);
        if (price is null)
            return NotFound();

        db.Prices.Remove(price);
        await db.SaveChangesAsync();

        return NoContent();
    }

    private static PriceDto ToDto(Price p) => new(p.Id, p.AssetId, p.Value, p.Date);
}
