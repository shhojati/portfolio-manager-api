using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Auth;

namespace PortfolioManager.Api.RateLimiting;

public static class RateLimitPolicies
{
    /// <summary>Data endpoints: a budget per signed-in user (per IP address when anonymous).</summary>
    public const string Api = "api";

    /// <summary>Endpoints that check a password: a small budget per IP address, against password guessing.</summary>
    public const string Auth = "auth";
}

/// <summary>Request budgets, bound from the "RateLimiting" section.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    public WindowLimit ApiClient { get; set; } = new() { PermitLimit = 120 };

    /// <summary>The backoffice makes a few requests per page, so admins get more headroom.</summary>
    public WindowLimit Admin { get; set; } = new() { PermitLimit = 600 };

    /// <summary>
    /// Callers without a token, per IP address: the public endpoints (asset search and lookup by
    /// identifier, an asset's price history, latest prices)
    /// and any rejected request to a protected one share this budget.
    /// </summary>
    public WindowLimit Anonymous { get; set; } = new() { PermitLimit = 30 };

    public WindowLimit Auth { get; set; } = new() { PermitLimit = 10 };
}

public sealed class WindowLimit
{
    public int PermitLimit { get; set; }
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

public static class RateLimitSetup
{
    public static void AddPortfolioRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                var message = "Too many requests. Try again later.";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                    response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    message = $"Too many requests. Try again in {seconds} seconds.";
                }

                await response.WriteAsJsonAsync(
                    new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = message },
                    options: null, contentType: "application/problem+json", ct);
            };

            options.AddPolicy(RateLimitPolicies.Api, context =>
            {
                var limits = Limits(context);
                var user = context.User;
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                return userId is null
                    ? FixedWindow(limits, limits.Anonymous, $"ip:{ClientIp(context)}")
                    : FixedWindow(limits, user.IsInRole(Roles.Admin) ? limits.Admin : limits.ApiClient, $"user:{userId}");
            });

            options.AddPolicy(RateLimitPolicies.Auth, context =>
            {
                var limits = Limits(context);
                return FixedWindow(limits, limits.Auth, $"ip:{ClientIp(context)}");
            });
        });
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitOptions>>().CurrentValue;

    // Behind a reverse proxy this is only the client's address when forwarded headers are enabled
    // (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, set in the Dockerfiles).
    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> FixedWindow(RateLimitOptions options, WindowLimit limit, string key) =>
        options.Enabled
            ? RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = limit.Window,
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter(key);
}
