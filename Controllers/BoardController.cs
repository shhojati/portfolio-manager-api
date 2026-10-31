using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PortfolioManager.Api.Auth;
using PortfolioManager.Api.Board;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.RateLimiting;

namespace PortfolioManager.Api.Controllers;

/// <summary>
/// Latest prices with their daily change and a short trend, a list at a time, for price boards such as the Chandoo site
/// (chandoo/). Read-only; for API clients and admins.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Readers)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public class BoardController(PriceBoard board) : ControllerBase
{
    private const int MaxPageSize = 100;
    private const int MaxIdentifiers = 100;
    private const int MaxSearchResults = 50;

    /// <summary>One page of the assets of the given types that have a price.</summary>
    /// <param name="types">Asset types, e.g. Currency, or Gold and Coin.</param>
    /// <param name="order">"name" for Persian alphabetical order by symbol; otherwise tgju's instruments in their own order
    /// (dollar first, 18 karat gold first) and the rest in the order they were added.</param>
    /// <param name="activeDays">Only assets with a price in this many days (so delisted stocks drop off); 0 for all.</param>
    [HttpGet]
    public async Task<ActionResult<BoardPageDto>> Get(
        [FromQuery] string[] types,
        [FromQuery] string? order,
        [FromQuery, Range(0, 3660)] int activeDays = 30,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, MaxPageSize)] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (types.Length == 0)
            return BadRequest("At least one asset type is required.");
        var byName = string.Equals(order, "name", StringComparison.OrdinalIgnoreCase);
        return await board.GetPageAsync(types, byName, activeDays == 0 ? null : activeDays, page, pageSize, ct);
    }

    /// <summary>The assets with these identifiers, in the order given. Ones that don't exist or have no price are left out.</summary>
    [HttpGet("by-identifiers")]
    public async Task<ActionResult<IReadOnlyList<BoardItemDto>>> ByIdentifiers([FromQuery] string[] identifiers, CancellationToken ct)
    {
        if (identifiers.Length == 0)
            return BadRequest("At least one identifier is required.");
        if (identifiers.Length > MaxIdentifiers)
            return BadRequest($"At most {MaxIdentifiers} identifiers per request.");
        return Ok(await board.GetByIdentifiersAsync(identifiers, ct));
    }

    /// <summary>The assets matching a search (as GET /api/assets/search) that have a price, best match first.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<BoardItemDto>>> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Search query 'q' is required.");
        return Ok(await board.SearchAsync(q.Trim()[..Math.Min(q.Trim().Length, 100)], MaxSearchResults, ct));
    }
}
