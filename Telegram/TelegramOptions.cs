using PortfolioManager.Api.Bale;

namespace PortfolioManager.Api.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public long[] AdminUserIds { get; set; } = [];
    public TimeOnly StartTime { get; set; } = new(9, 0);
    public TimeOnly EndTime { get; set; } = new(18, 0);
    public DayOfWeek[] DaysOfWeek { get; set; } = [];
    public int IntervalMinutes { get; set; } = 30;
    public string[] AssetIdentifiers { get; set; } = [];

    // Use the same Tehran window/overnight semantics as the existing Bale publisher.
    public BaleOptions Schedule => new()
    {
        StartTime = StartTime, EndTime = EndTime, DaysOfWeek = DaysOfWeek,
        IntervalMinutes = IntervalMinutes
    };
}
