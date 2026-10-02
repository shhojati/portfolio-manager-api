using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Auth;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.Models;
using PortfolioManager.Api.RateLimiting;

namespace PortfolioManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public class AuthController(UserService users, IOptionsMonitor<BearerTokenOptions> bearerOptions, TimeProvider time) : ControllerBase
{
    private const string InvalidCredentials = "Invalid username or password.";

    /// <summary>Signs an admin in to the backoffice (sets the session cookie).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<UserDto>> Login(LoginRequest request)
    {
        var user = await users.VerifyAsync(request.Username, request.Password);
        if (user is null)
            return Unauthorized(InvalidCredentials);
        if (user.Role != Roles.Admin)
            return StatusCode(StatusCodes.Status403Forbidden, "This account can't sign in to the backoffice. API clients get a token from POST /api/auth/token.");

        await HttpContext.SignInAsync(AuthSchemes.Cookie, UserService.CreatePrincipal(user, AuthSchemes.Cookie),
            new AuthenticationProperties { IsPersistent = request.RememberMe });
        return ToDto(user);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Cookie);
        return NoContent();
    }

    /// <summary>The signed-in user.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await users.FindCurrentAsync(User);
        return user is null ? Unauthorized() : ToDto(user);
    }

    /// <summary>Exchanges a username and password for a bearer token.</summary>
    /// <remarks>
    /// Send the token as <c>Authorization: Bearer {accessToken}</c> (or <c>?access_token=</c> for /ws).
    /// When it expires, get a new one from /api/auth/refresh with the refresh token.
    /// </remarks>
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Token(TokenRequest request)
    {
        var user = await users.VerifyAsync(request.Username, request.Password);
        if (user is null)
            return Unauthorized(InvalidCredentials);

        return SignIn(UserService.CreatePrincipal(user, AuthSchemes.Bearer), AuthSchemes.Bearer);
    }

    /// <summary>Exchanges a refresh token for a new access token (and refresh token).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        var ticket = bearerOptions.Get(AuthSchemes.Bearer).RefreshTokenProtector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || time.GetUtcNow() >= expires)
            return Unauthorized("The refresh token is invalid or has expired.");

        // Refuse if the user was deleted or changed their password since the token was issued.
        var user = await users.FindCurrentAsync(ticket.Principal);
        if (user is null)
            return Unauthorized("The refresh token is invalid or has expired.");

        return SignIn(UserService.CreatePrincipal(user, AuthSchemes.Bearer), AuthSchemes.Bearer);
    }

    /// <summary>Changes the signed-in user's password. Their other sessions and refresh tokens stop working.</summary>
    [HttpPost("password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, [FromServices] Data.AppDbContext db)
    {
        var user = await users.FindCurrentAsync(User);
        if (user is null)
            return Unauthorized();
        if (!users.CheckPassword(user, request.CurrentPassword))
            return BadRequest("The current password is incorrect.");

        users.SetPassword(user, request.NewPassword);
        await db.SaveChangesAsync();

        // Keep this browser signed in with the new security stamp.
        if (User.Identity?.AuthenticationType == AuthSchemes.Cookie)
            await HttpContext.SignInAsync(AuthSchemes.Cookie, UserService.CreatePrincipal(user, AuthSchemes.Cookie));

        return NoContent();
    }

    internal static UserDto ToDto(User u) => new(u.Id, u.Username, u.Role, u.CreatedAt, u.LastLoginAt);
}
