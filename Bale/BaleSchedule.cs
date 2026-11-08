namespace PortfolioManager.Api.Bale;

public static class BaleSchedule
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    // End is exclusive. Overnight windows belong to the day on which they started.
    // Equal start/end means a full 24-hour window.
    public static DateTimeOffset? GetSlot(DateTimeOffset utcNow, BaleOptions options)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, Tehran);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var date = local.Date;
        if (options.StartTime >= options.EndTime && time < options.StartTime)
            date = date.AddDays(-1);

        var start = date.Add(options.StartTime.ToTimeSpan());
        var end = date.Add(options.EndTime.ToTimeSpan());
        if (end <= start) end = end.AddDays(1);
        if (!options.DaysOfWeek.Contains(date.DayOfWeek) || local.DateTime < start || local.DateTime >= end)
            return null;

        var slot = (long)((local.DateTime - start).TotalMinutes / options.IntervalMinutes);
        var slotStart = start.AddMinutes(slot * options.IntervalMinutes);
        return new DateTimeOffset(slotStart, Tehran.GetUtcOffset(slotStart));
    }
}
