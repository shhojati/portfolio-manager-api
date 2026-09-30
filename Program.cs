using System.Net;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Bitpin;
using PortfolioManager.Api.Data;
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
    .AllowAnyMethod()));

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply pending migrations (creates portfolio.db on first run)
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// Swagger is always on in Development; elsewhere it follows "Swagger:Enabled".
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseAuthorization();
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
});

app.Run();
