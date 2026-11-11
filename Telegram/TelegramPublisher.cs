using Microsoft.Extensions.Options;
using PortfolioManager.Api.Bale;
using PortfolioManager.Api.Board;

namespace PortfolioManager.Api.Telegram;

// One coordinator serializes webhook and scheduled sends within this server process.
public sealed class TelegramPublisher(
    IServiceScopeFactory scopes, IOptions<TelegramOptions> options,
    ILogger<TelegramPublisher> logger) : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<long, DateTimeOffset> updates = new();
    private DateTimeOffset? lastSlot;

    public async Task PublishAsync(long? updateId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var settings = options.Value;
            if (!settings.Enabled) return;
            var now = DateTimeOffset.UtcNow;
            foreach (var id in updates.Where(p => p.Value < now.AddDays(-1)).Select(p => p.Key).ToArray())
                updates.Remove(id);

            DateTimeOffset? slot = null;
            if (updateId is { } idValue)
            {
                if (updates.ContainsKey(idValue)) return;
                updates[idValue] = now;
            }
            else
            {
                slot = BaleSchedule.GetSlot(now, settings.Schedule);
                if (slot is null || slot == lastSlot) return;
                lastSlot = slot;
            }

            // Record attempts before sending: an ambiguous timeout must not repost the same batch.
            try
            {
                using var scope = scopes.CreateScope();
                var prices = await scope.ServiceProvider.GetRequiredService<PriceBoard>()
                    .GetByIdentifiersAsync(settings.AssetIdentifiers, ct);
                var timestamp = slot ?? TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"));
                var client = scope.ServiceProvider.GetRequiredService<TelegramClient>();
                foreach (var message in BalePriceMessages.Create(prices, timestamp))
                {
                    if (slot.HasValue && BaleSchedule.GetSlot(DateTimeOffset.UtcNow, settings.Schedule) != slot) break;
                    await client.SendAsync(message, ct);
                }
                logger.LogInformation("Telegram price publication completed: {PriceCount} prices", prices.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Telegram price publication failed ({ErrorType}); batch will not be retried", ex.GetType().Name);
            }
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            do { await PublishAsync(null, stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
