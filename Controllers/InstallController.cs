using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PortfolioManager.Api.Auth;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.RateLimiting;

namespace PortfolioManager.Api.Controllers;

[ApiController]
[Route("api/install")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Install)]
public class InstallController(InstallTokenService tokens) : ControllerBase
{
    /// <summary>Gives an install of the mobile app an anonymous token, for a rate limit budget of its own.</summary>
    /// <remarks>
    /// Send it back as <c>X-Install-Token</c> on every request. It takes nothing from the caller and
    /// nothing is stored: the token holds only a random id and its expiry. It grants no access; the
    /// public endpoints just count its requests separately from the caller's IP address. When a
    /// response carries <c>X-Install-Token: invalid</c>, get a new one.
    /// </remarks>
    [HttpPost]
    public InstallTokenResponse Create()
    {
        var (token, expiresAt) = tokens.Issue();
        return new InstallTokenResponse(token, expiresAt);
    }
}
