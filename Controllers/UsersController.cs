using System.Security.Claims;
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
[Authorize(Roles = Roles.Admin)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public class UsersController(AppDbContext db, UserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
        return await db.Users
            .OrderBy(u => u.Username)
            .Select(u => new UserDto(u.Id, u.Username, u.Role, u.CreatedAt, u.LastLoginAt))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request)
    {
        if (!Roles.IsValid(request.Role))
            return BadRequest($"Role must be one of: {string.Join(", ", Roles.All)}.");

        var username = request.Username.Trim();
        if (await db.Users.AnyAsync(u => u.Username == username))
            return Conflict($"User '{username}' already exists.");

        var user = new User { Username = username, Role = request.Role };
        users.SetPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), null, AuthController.ToDto(user));
    }

    /// <summary>Sets a user's password, signing out their sessions and invalidating their refresh tokens.</summary>
    [HttpPut("{id:int}/password")]
    public async Task<IActionResult> SetPassword(int id, SetPasswordRequest request)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        users.SetPassword(user, request.Password);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return NotFound();
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) == id.ToString())
            return BadRequest("You can't delete your own account.");
        if (user.Role == Roles.Admin && await db.Users.CountAsync(u => u.Role == Roles.Admin) == 1)
            return BadRequest("You can't delete the last admin.");

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
