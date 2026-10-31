using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using PortfolioManager.Api.Auth;
using PortfolioManager.Api.Bitpin;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Fipiran;
using PortfolioManager.Api.History;
using PortfolioManager.Api.Ime;
using PortfolioManager.Api.Inflation;
using PortfolioManager.Api.RateLimiting;
using PortfolioManager.Api.Realtime;
using PortfolioManager.Api.Tgju;
using PortfolioManager.Api.Tsetmc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// CORS for the web client. Origins come from "Cors:AllowedOrigins"; in Development any localhost origin is also allowed.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(origin =>
        allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase) ||
        (builder.Environment.IsDevelopment() && Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback))
    .AllowAnyHeader()
    .AllowAnyMethod()
    // Lets a browser app see that its install token was refused (see Auth/InstallTokens.cs).
    .WithExposedHeaders(InstallTokenService.HeaderName)));

// Real-time push: the hub is both a singleton (for publishing) and a hosted service (for sending).
builder.Services.AddSingleton<WebSocketHub>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WebSocketHub>());
builder.Services.AddSingleton<RealtimeBroadcastInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) => options
    .UseSqlite(builder.Configuration.GetConnectionString("Default"))
    .AddInterceptors(sp.GetRequiredService<RealtimeBroadcastInterceptor>()));

// Background import of stock/ETF prices from tsetmc.com.
builder.Services.Configure<TsetmcOptions>(builder.Configuration.GetSection(TsetmcOptions.SectionName));
builder.Services.AddHttpClient<TsetmcClient>(http =>
    {
        http.BaseAddress = new Uri("https://cdn.tsetmc.com/");
        http.Timeout = TimeSpan.FromSeconds(60);
        // TSETMC answers 403 to requests without a browser-like User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PortfolioManager/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
builder.Services.AddHostedService<TsetmcPriceSyncService>();

// Background import of TSE/IFB options and stock futures from the same API, on a shorter schedule.
builder.Services.Configure<TsetmcDerivativesOptions>(builder.Configuration.GetSection(TsetmcDerivativesOptions.SectionName));
builder.Services.AddHostedService<TsetmcDerivativesSyncService>();

// Background import of crypto prices (in Toman, stored as Rial) from bitpin.ir.
builder.Services.Configure<BitpinOptions>(builder.Configuration.GetSection(BitpinOptions.SectionName));
builder.Services.AddHttpClient<BitpinClient>(http =>
    {
        http.BaseAddress = new Uri("https://api.bitpin.ir/");
        http.Timeout = TimeSpan.FromSeconds(60);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
builder.Services.AddHostedService<BitpinPriceSyncService>();

// Background import of world currency, gold, coin and metal prices (in Rial) from tgju.org.
builder.Services.Configure<TgjuOptions>(builder.Configuration.GetSection(TgjuOptions.SectionName));
builder.Services.AddHttpClient<TgjuClient>(http =>
    {
        // No base address: TgjuClient picks one of tgju's mirrors and times out each attempt itself.
        http.Timeout = TimeSpan.FromMinutes(2);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PortfolioManager/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
builder.Services.AddHostedService<TgjuPriceSyncService>();

// Background import of issuance/redemption mutual fund NAVs (in Rial) from fipiran.ir.
builder.Services.Configure<FipiranOptions>(builder.Configuration.GetSection(FipiranOptions.SectionName));
builder.Services.AddHttpClient<FipiranClient>(http =>
    {
        http.BaseAddress = new Uri("https://www.fipiran.ir/");
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PortfolioManager/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
builder.Services.AddHostedService<FipiranPriceSyncService>();

// Background import of the commodity exchange's continuous gold and silver deposit certificates (in Rial) from ime.co.ir.
builder.Services.Configure<ImeOptions>(builder.Configuration.GetSection(ImeOptions.SectionName));
builder.Services.AddHttpClient<ImeClient>(http =>
    {
        http.BaseAddress = new Uri("https://dataapi.ime.co.ir/");
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PortfolioManager/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
builder.Services.AddHostedService<ImePriceSyncService>();

// Background import of the commodity exchange's options (in Rial) from its own site's daily report.
builder.Services.Configure<ImeDerivativesOptions>(builder.Configuration.GetSection(ImeDerivativesOptions.SectionName));
builder.Services.AddHttpClient<ImeDerivativesClient>(http =>
    {
        http.BaseAddress = new Uri("https://www.ime.co.ir/");
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PortfolioManager/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
builder.Services.AddHostedService<ImeDerivativesSyncService>();

// Iran's consumer price index, from a table kept by hand (Inflation/CpiTable.cs), stored once at startup.
builder.Services.AddHostedService<InflationSyncService>();

// Past prices fetched from the same sources, on demand, when the history endpoint asks for days before what's stored.
builder.Services.Configure<PriceHistoryOptions>(builder.Configuration.GetSection(PriceHistoryOptions.SectionName));
builder.Services.AddSingleton<PriceHistoryBackfill>();

// Admins sign in with a cookie, API clients with a bearer token; see Auth/AuthSetup.cs.
builder.AddPortfolioAuth();
builder.AddPortfolioRateLimiting();

// Anonymous install tokens give each install of the mobile app a rate limit budget of its own.
builder.Services.Configure<InstallTokenOptions>(builder.Configuration.GetSection(InstallTokenOptions.SectionName));
builder.Services.AddSingleton<InstallTokenService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Lets Swagger UI call the API with a token from POST /api/auth/token (a signed-in admin's cookie also works).
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        Description = "Access token from POST /api/auth/token.",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

var app = builder.Build();

// Apply pending migrations (creates portfolio.db on first run)
// and create the users configured under "Auth:Users" that don't exist yet.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    await UserSeeder.SeedAsync(scope.ServiceProvider);
}

// Swagger is always on in Development; elsewhere it follows "Swagger:Enabled".
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();

// The backoffice page itself is only served to signed-in admins; everyone else gets the login page.
app.Use(async (context, next) =>
{
    if (context.Request.Path.Value is "/" or "/index.html" && !context.User.IsInRole(Roles.Admin))
    {
        context.Response.Redirect("login.html");
        return;
    }
    await next();
});

// The backoffice UI (wwwroot) is served at the site root. "no-cache" makes browsers revalidate,
// so a deploy is picked up immediately; unchanged files still come back as 304s.
// Static files come before UseAuthorization so the login page and its scripts stay public.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

app.UseInstallTokens();
app.UseRateLimiter();
app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.MapControllers();

// Clients connect here to receive "asset.created", "price.created" and "price.updated" messages.
app.Map("/ws", async (HttpContext context, WebSocketHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.HandleConnectionAsync(socket, context.RequestAborted);
}).RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Readers });

app.Run();
