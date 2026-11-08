using Microsoft.Extensions.Options;
using PortfolioManager.Api.Board;

namespace PortfolioManager.Api.Bale;

public sealed class BalePricePublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<BaleOptions> options,
    ILogger<BalePricePublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Bale price publishing is disabled");
            return;
        }

        DateTimeOffset? lastAttempt = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            do
            {
                var slot = BaleSchedule.GetSlot(DateTimeOffset.UtcNow, settings);
                if (slot is null || slot == lastAttempt) continue;
                // A failed/ambiguous request is not retried in the same slot, to avoid duplicate posts.
                lastAttempt = slot;
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var board = scope.ServiceProvider.GetRequiredService<PriceBoard>();
                    var prices = await board.GetByIdentifiersAsync(settings.AssetIdentifiers, stoppingToken);
                    var messages = BalePriceMessages.Create(prices, slot.Value);
                    var client = scope.ServiceProvider.GetRequiredService<BaleClient>();
                    foreach (var message in messages)
                    {
                        if (BaleSchedule.GetSlot(DateTimeOffset.UtcNow, settings) != slot) break;
                        await client.SendAsync(settings, message, stoppingToken);
                    }
                    logger.LogInformation("Bale publishing cycle completed for {Slot}: {PriceCount} prices", slot, prices.Count);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Exception messages from HttpClient may contain the secret URL.
                    logger.LogWarning("Bale publishing failed ({ErrorType}); next attempt is in the next scheduled interval", ex.GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
