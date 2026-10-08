using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace PortfolioManager.Api.Auth;

/// <summary>Settings for the mobile app's anonymous install tokens, bound from the "Install" section.</summary>
public sealed class InstallTokenOptions
{
    public const string SectionName = "Install";

    /// <summary>How long an install token is accepted. The app gets a new one (with a new id) before then.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromDays(180);
}

/// <summary>
/// Anonymous install tokens. Each install of the mobile app gets a random id from POST /api/install,
/// sealed with the data protection keys, and sends it back in the <see cref="HeaderName"/> header so it
/// gets a rate limit budget of its own instead of sharing its IP address's. Nothing about the user or
/// the device goes into a token, and nothing is stored on the server. A token isn't a sign-in: it
/// grants no access, it only picks the rate limit partition.
/// </summary>
public sealed class InstallTokenService(
    IDataProtectionProvider dataProtection, IOptionsMonitor<InstallTokenOptions> options, TimeProvider time)
{
    public const string HeaderName = "X-Install-Token";

    /// <summary>Sent back in <see cref="HeaderName"/> when a request's token can't be read, so the app asks for a new one.</summary>
    public const string InvalidValue = "invalid";

    // Real tokens are about 180 characters; anything much longer isn't worth decrypting.
    private const int MaxTokenLength = 512;

    private readonly ITimeLimitedDataProtector protector = dataProtection
        .CreateProtector("PortfolioManager.InstallToken.v1")
        .ToTimeLimitedDataProtector();

    public (string Token, DateTimeOffset ExpiresAt) Issue()
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var expiresAt = time.GetUtcNow() + options.CurrentValue.TokenLifetime;
        return (protector.Protect(id, expiresAt), expiresAt);
    }

    /// <summary>The install id in the token; false if it is malformed, forged, expired or sealed with lost keys.</summary>
    public bool TryRead(string token, [NotNullWhen(true)] out string? id)
    {
        id = null;
        if (token.Length > MaxTokenLength)
            return false;
        try
        {
            id = protector.Unprotect(token, out _);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}

public static class InstallTokenMiddleware
{
    private static readonly object InstallIdKey = new();

    /// <summary>The install id from the request's valid install token, or null.</summary>
    public static string? InstallId(this HttpContext context) => context.Items[InstallIdKey] as string;

    /// <summary>
    /// Reads the install token for the rate limiter, so it must come before UseRateLimiter. A request
    /// with an unreadable token isn't refused: it is treated as one without a token, and the response
    /// says so in the same header.
    /// </summary>
    public static IApplicationBuilder UseInstallTokens(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.Headers[InstallTokenService.HeaderName] is [{ Length: > 0 } token])
        {
            var tokens = context.RequestServices.GetRequiredService<InstallTokenService>();
            if (tokens.TryRead(token, out var id))
                context.Items[InstallIdKey] = id;
            else
                context.Response.Headers[InstallTokenService.HeaderName] = InstallTokenService.InvalidValue;
        }
        await next();
    });
}
