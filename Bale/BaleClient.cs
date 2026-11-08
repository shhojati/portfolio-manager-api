using System.Net.Http.Json;
using System.Text.Json;

namespace PortfolioManager.Api.Bale;

public sealed class BaleClient(HttpClient http)
{
    public async Task SendAsync(BaleOptions options, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            $"https://tapi.bale.ai/bot{options.BotToken}/sendMessage",
            new { chat_id = options.ChannelId, text }, ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode ||
            !body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            // Do not log the request URI (it contains the token) or server-supplied text.
            var code = body.RootElement.TryGetProperty("error_code", out var error) ? error.ToString() : "unknown";
            throw new InvalidOperationException($"Bale rejected message: HTTP {(int)response.StatusCode}, code {code}.");
        }
    }
}
