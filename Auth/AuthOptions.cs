namespace PortfolioManager.Api.Auth;

/// <summary>Settings for sign-in and tokens, bound from the "Auth" section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>How long a backoffice session lasts without activity (it slides while in use).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Lifetime of an API access token.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Lifetime of a refresh token, which exchanges for a new access token without the password.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Users created on startup if they don't exist yet. Existing users are never changed, so after the
    /// first run the passwords can be removed from configuration and managed in the backoffice.
    /// </summary>
    public List<SeedUser> Users { get; set; } = [];
}

public sealed class SeedUser
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
