using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Telegram;

namespace PortfolioManager.Api.Controllers;

[ApiController]
[Route("api/telegram/webhook")]
public sealed class TelegramController(IOptions<TelegramOptions> options, TelegramPublisher publisher) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous] // Telegram authenticates with its webhook secret, not our user cookie/token.
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Receive(TelegramUpdate update)
    {
        var settings = options.Value;
        if (!settings.Enabled) return NotFound();
        var secret = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
        if (string.IsNullOrEmpty(settings.WebhookSecret) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(settings.WebhookSecret)))
            return Unauthorized();

        // Only explicit commands from configured admins in private chats can post to the channel.
        var message = update.Message;
        if (message?.Chat?.Type != "private" || message.From is null ||
            !settings.AdminUserIds.Contains(message.From.Id) || message.Chat.Id != message.From.Id ||
            message.Text?.Trim() != "/prices") return Ok();

        // Let work finish independently of a Telegram disconnect. Re-deliveries share the update ID.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await publisher.PublishAsync(update.UpdateId, timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        return Ok();
    }
}

public sealed record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    TelegramMessage? Message);
public sealed record TelegramMessage(string? Text, TelegramUser? From, TelegramChat? Chat);
public sealed record TelegramUser(long Id);
public sealed record TelegramChat(long Id, string? Type);
