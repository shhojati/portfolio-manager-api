using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Auth;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.Fees;
using PortfolioManager.Api.Models;
using PortfolioManager.Api.RateLimiting;

namespace PortfolioManager.Api.Controllers;

/// <summary>
/// Exchange fee rates by kind of asset and the day they took effect. The mobile app figures a trade's fee from the rate
/// in effect on the trade's day; admins set the rates in the backoffice.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public class FeesController(AppDbContext db) : ControllerBase
{
    /// <summary>Every kind of asset with its rates, oldest first. Public: no sign-in needed, rate limited per IP.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<FeeClassDto>>> GetAll()
    {
        var rates = await db.FeeRates.ToListAsync();
        return FeeClasses.All
            .Select(c => new FeeClassDto(c.Key, c.Label, rates
                .Where(r => r.Class == c.Key)
                .OrderBy(r => r.EffectiveFrom)
                .Select(ToDto)
                .ToList()))
            .ToList();
    }

    [HttpPost]
    public async Task<ActionResult<FeeRateDto>> Create(FeeRateRequest request)
    {
        if (await Invalid(request, null) is { } error)
            return error;

        var rate = new FeeRate { Class = request.Class };
        Apply(rate, request);
        db.FeeRates.Add(rate);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), null, ToDto(rate));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<FeeRateDto>> Update(int id, FeeRateRequest request)
    {
        var rate = await db.FeeRates.FindAsync(id);
        if (rate is null)
            return NotFound();
        if (await Invalid(request, id) is { } error)
            return error;

        rate.Class = request.Class;
        Apply(rate, request);
        await db.SaveChangesAsync();
        return ToDto(rate);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var rate = await db.FeeRates.FindAsync(id);
        if (rate is null)
            return NotFound();

        db.FeeRates.Remove(rate);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ActionResult?> Invalid(FeeRateRequest request, int? id)
    {
        if (!FeeClasses.IsValid(request.Class))
            return BadRequest($"Class must be one of: {string.Join(", ", FeeClasses.All.Select(c => c.Key))}.");
        if (await db.FeeRates.AnyAsync(r => r.Class == request.Class && r.EffectiveFrom == request.EffectiveFrom && r.Id != id))
            return Conflict($"{request.Class} already has a rate from {request.EffectiveFrom:yyyy-MM-dd}; edit that one instead.");
        return null;
    }

    private static void Apply(FeeRate rate, FeeRateRequest request)
    {
        rate.EffectiveFrom = request.EffectiveFrom;
        rate.BuyPercent = request.BuyPercent;
        rate.SellPercent = request.SellPercent;
    }

    private static FeeRateDto ToDto(FeeRate r) => new(r.Id, r.EffectiveFrom, r.BuyPercent, r.SellPercent);
}
