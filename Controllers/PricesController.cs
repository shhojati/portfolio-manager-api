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
    private const int MaxHistoryDays = 3660;
    private const int MaxHistoryPoints = 200_000;

    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

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

    /// <summary>Get the price of each asset in the given list of asset identifiers on every day of a date range.</summary>
    /// <remarks>
    /// One price per asset for each day from <c>from</c> to <c>to</c> (default: today, Tehran time): the last one
    /// recorded that day; on a day without one, like a market holiday, the last one recorded before it; and 0 before
    /// the asset's first price. Identifiers with no matching asset are omitted.
    /// Public: no sign-in needed, rate limited per IP; at most 100 identifiers and 3660 days, and 200,000 prices in
    /// all, per request.
    /// </remarks>
    [HttpGet("history")]
    [AllowAnonymous]
    public async Task<ActionResult<List<AssetPriceHistoryDto>>> GetHistory(
        [FromQuery] List<string> identifiers,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly? to)
    {
        var ids = identifiers.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest("At least one identifier is required.");
        if (ids.Count > MaxIdentifiers)
            return BadRequest($"At most {MaxIdentifiers} identifiers per request.");

        var last = to ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran));
        if (last < from)
            return BadRequest("'to' can't be before 'from'.");
        var days = last.DayNumber - from.DayNumber + 1;
        if (days > MaxHistoryDays)
            return BadRequest($"At most {MaxHistoryDays} days per request.");
        if (days * ids.Count > MaxHistoryPoints)
            return BadRequest($"At most {MaxHistoryPoints} prices per request: ask for fewer identifiers or days.");

        var assets = await db.Assets
            .Where(a => ids.Contains(a.Identifier))
            .Select(a => new { a.Id, a.Identifier })
            .ToListAsync();
        var assetIds = assets.Select(a => a.Id).ToList();

        // Prices are dated by their Tehran day, stored as midnight UTC.
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = last.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        // The range's prices, and each asset's last before it: its price on the range's first days.
        var rows = await db.Prices
            .Where(p => assetIds.Contains(p.AssetId) && p.Date <= end
                && (p.Date >= start
                    || p.Date == db.Prices.Where(q => q.AssetId == p.AssetId && q.Date < start).Max(q => q.Date)))
            .Select(p => new { p.Id, p.AssetId, p.Date, p.Value })
            .ToListAsync();

        // Several prices can share a date; the most recently inserted one is that day's.
        var byAsset = rows
            .GroupBy(r => r.AssetId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => DateOnly.FromDateTime(r.Date))
                    .Select(d => (Date: d.Key, d.MaxBy(r => r.Id)!.Value))
                    .OrderBy(d => d.Date)
                    .ToList());

        return assets
            .OrderBy(a => ids.IndexOf(a.Identifier))
            .Select(a =>
            {
                var known = byAsset.GetValueOrDefault(a.Id) ?? [];
                var prices = new List<DayPriceDto>(days);
                decimal value = 0;
                var next = 0;
                for (var day = from; day <= last; day = day.AddDays(1))
                {
                    while (next < known.Count && known[next].Date <= day)
                        value = known[next++].Value;
                    prices.Add(new DayPriceDto(day, value));
                }
                return new AssetPriceHistoryDto(a.Identifier, prices);
            })
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
