using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PortfolioManager.Api.Telegram;

public sealed class TelegramClient(HttpClient http, IOptions<TelegramOptions> options)
{
    public async Task SendAsync(string text, CancellationToken ct)
    {
        var settings = options.Value;
        using var response = await http.PostAsJsonAsync(
            $"https://api.telegram.org/bot{settings.BotToken}/sendMessage",
            new { chat_id = settings.ChannelId, text }, ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode ||
            !body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            // Never include Telegram's response or the token-bearing URL in exceptions.
            throw new InvalidOperationException($"Telegram send failed (HTTP {(int)response.StatusCode}).");
    }
}
