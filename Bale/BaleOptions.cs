namespace PortfolioManager.Api.Bale;

public sealed class BaleOptions
{
    public const string SectionName = "Bale";
    public bool Enabled { get; set; }
    public TimeOnly StartTime { get; set; } = new(9, 0);
    public TimeOnly EndTime { get; set; } = new(18, 0);
    public DayOfWeek[] DaysOfWeek { get; set; } =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];
    public int IntervalMinutes { get; set; } = 30;
    public string BotToken { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string[] AssetIdentifiers { get; set; } = ["USD", "EUR", "GOLD-18", "COIN-EMAMI"];
}
