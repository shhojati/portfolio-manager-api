using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Auth;

public static class AuthSetup
{
    /// <summary>
    /// Backoffice users sign in with a cookie; API clients exchange a password for a bearer token.
    /// Every endpoint requires a signed-in user unless it opts out with [AllowAnonymous].
    /// </summary>
    public static void AddPortfolioAuth(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(AuthOptions.SectionName);
        builder.Services.Configure<AuthOptions>(section);
        var options = section.Get<AuthOptions>() ?? new AuthOptions();

        // Cookies and tokens are encrypted with data protection keys. Keep them on the persistent disk so
        // sessions and tokens survive restarts and deploys.
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName("PortfolioManager");
        if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        builder.Services.AddScoped<UserService>();

        builder.Services.AddAuthentication(AuthSchemes.Default)
            .AddPolicyScheme(AuthSchemes.Default, "Cookie or bearer token", o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    || IsWebSocketWithToken(context.Request)
                    ? AuthSchemes.Bearer
                    : AuthSchemes.Cookie)
            .AddCookie(AuthSchemes.Cookie, o =>
            {
                o.Cookie.Name = "pm.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict; // also what keeps other sites from making requests with it
                o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = options.SessionLifetime;
                o.SlidingExpiration = true;

                // This is an API: answer with status codes instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = context => SetStatus(context, StatusCodes.Status401Unauthorized);
                o.Events.OnRedirectToAccessDenied = context => SetStatus(context, StatusCodes.Status403Forbidden);

                // End the session once the user is deleted or their password changes.
                o.Events.OnValidatePrincipal = async context =>
                {
                    var users = context.HttpContext.RequestServices.GetRequiredService<UserService>();
                    if (await users.FindCurrentAsync(context.Principal) is null)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(AuthSchemes.Cookie);
                    }
                };
            })
            .AddBearerToken(AuthSchemes.Bearer, o =>
            {
                o.BearerTokenExpiration = options.AccessTokenLifetime;
                o.RefreshTokenExpiration = options.RefreshTokenLifetime;

                // Browsers can't set headers on a WebSocket, so /ws also accepts ?access_token=.
                o.Events.OnMessageReceived = context =>
                {
                    if (IsWebSocketWithToken(context.Request))
                        context.Token = context.Request.Query["access_token"];
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    }

    private static bool IsWebSocketWithToken(HttpRequest request) =>
        request.Path.StartsWithSegments("/ws") && request.Query.ContainsKey("access_token");

    private static Task SetStatus(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
