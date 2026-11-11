# Telegram channel prices on your server

This integration runs inside the existing ASP.NET API on your server. It does not need a serverless host or long polling. The background job sends prices on a Tehran-time schedule; Telegram calls `POST /api/telegram/webhook` for incoming bot messages.

## Server configuration

Create a bot with Telegram's @BotFather, add it as a channel administrator and grant permission to post messages. Configure these environment variables on the API server (keep secrets out of source control):

```text
Telegram__Enabled=true
Telegram__BotToken=<bot token>
Telegram__ChannelId=@your_channel
Telegram__WebhookSecret=<random secret of 1-256 letters/digits/_/->
Telegram__AdminUserIds__0=<your numeric Telegram user ID>
```

A private channel can use its numeric `-100...` ID as `ChannelId`. Only allowlisted admins sending exactly `/prices` in a private conversation with the bot can trigger an immediate **channel post**. An empty admin list disables manual commands. Unknown updates are acknowledged without action; invalid secrets receive 401. No reply is sent to the private chat.

The `Telegram` section in `appsettings.json` controls `StartTime`, `EndTime`, `DaysOfWeek`, `IntervalMinutes` and `AssetIdentifiers` independently of Bale. Restart after changing configuration. Schedule semantics match Bale: end exclusive, overnight windows assigned to the starting day, equal start/end means 24 hours, current interval is posted at startup, missed intervals are not replayed. Manual commands can post outside the schedule.

Prices come from the existing database, formatted in Persian with Rial values and each quote's date. Missing prices are skipped; empty batches send nothing. The server needs outbound HTTPS access to `api.telegram.org`.

## Register the webhook

Expose the API at a public HTTPS URL with a valid certificate, normally on port 443. Preserve the request body and `X-Telegram-Bot-Api-Secret-Token` header through your reverse proxy. Use your normal forwarded-header configuration for HTTPS behind a proxy. The webhook uses its own secret and does not require an API bearer token.

After deployment, run this on a trusted PowerShell terminal with `Telegram__BotToken` and `Telegram__WebhookSecret` already set in that terminal's environment:

```powershell
$webhookBody = @{
    url = 'https://YOUR-API-DOMAIN/api/telegram/webhook'
    secret_token = $env:Telegram__WebhookSecret
    allowed_updates = @('message')
    max_connections = 1
} | ConvertTo-Json
Invoke-RestMethod -Method Post `
    -Uri "https://api.telegram.org/bot$($env:Telegram__BotToken)/setWebhook" `
    -ContentType 'application/json' -Body $webhookBody
```

Check that the response has `ok: true`. Check delivery status with:

```powershell
Invoke-RestMethod -Uri "https://api.telegram.org/bot$($env:Telegram__BotToken)/getWebhookInfo"
```

Send `/prices` privately from an allowlisted account and check the channel. Scheduled publishing works without registering the webhook. Registration is deliberately manual so local development never changes the production bot's webhook. Do not run a polling client for the same bot while using a webhook.

## Delivery behavior

Run one API instance for publishing. Scheduled interval attempts and webhook update IDs are tracked in memory; webhook IDs are retained for 24 hours. Concurrent sends are serialized. Restarting or running multiple instances can duplicate posts. Failures and ambiguous timeouts are logged by error type only and are not automatically retried for that interval/update, avoiding duplicate partial batches; the next scheduled interval or a new admin command can try again. A successful webhook acknowledgement does not guarantee a channel post: check server logs for failures. This is best-effort delivery, matching Bale's existing approach.

Reference: [Telegram Bot API — webhooks](https://core.telegram.org/bots/api#setwebhook).

## Verification

Run `dotnet run --project tests/TelegramChecks/TelegramChecks.csproj`. These checks use an in-memory SQLite database and a fake Telegram HTTP handler; they never contact Telegram or change the production database. They cover webhook secrets, admin/private-chat restrictions, duplicate concurrent updates, scheduled interval deduplication, database price delivery, failed-send redelivery and the disabled state.
