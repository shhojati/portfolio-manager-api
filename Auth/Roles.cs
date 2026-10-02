namespace PortfolioManager.Api.Auth;

public static class Roles
{
    /// <summary>Signs in to the backoffice and can read and change everything.</summary>
    public const string Admin = "Admin";

    /// <summary>Calls the API with a bearer token; read-only.</summary>
    public const string ApiClient = "ApiClient";

    /// <summary>Roles that may read assets and prices, for <c>[Authorize(Roles = ...)]</c>.</summary>
    public const string Readers = Admin + "," + ApiClient;

    public static readonly string[] All = [Admin, ApiClient];

    public static bool IsValid(string role) => All.Contains(role);
}

public static class AuthSchemes
{
    /// <summary>Picks <see cref="Bearer"/> when a token is sent, otherwise <see cref="Cookie"/>.</summary>
    public const string Default = "CookieOrBearer";

    /// <summary>The backoffice session, set by POST /api/auth/login.</summary>
    public const string Cookie = "Cookies";

    /// <summary>Tokens for API clients, issued by POST /api/auth/token.</summary>
    public const string Bearer = "BearerToken";
}
