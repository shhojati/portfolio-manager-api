using System.Globalization;

namespace PortfolioManager.Api.Public;

/// <summary>How the public pages write numbers, dates and times in Persian.</summary>
public static class PersianFormat
{
    private static readonly PersianCalendar Calendar = new();

    private static readonly string[] Months =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    private static readonly string[] Days = ["یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"];

    /// <summary>Latin digits replaced with Persian ones.</summary>
    public static string Digits(string text) =>
        string.Create(text.Length, text, (span, s) =>
        {
            for (var i = 0; i < s.Length; i++)
                span[i] = s[i] is >= '0' and <= '9' ? (char)('۰' + (s[i] - '0')) : s[i];
        });

    public static string Digits(int number) => Digits(number.ToString(CultureInfo.InvariantCulture));

    /// <summary>A stored price date (the Tehran day, at midnight UTC) as a Jalali date, e.g. «۹ آبان ۱۴۰۵».</summary>
    public static string Date(DateTime date) =>
        Digits($"{Calendar.GetDayOfMonth(date)} {Months[Calendar.GetMonth(date) - 1]} {Calendar.GetYear(date)}");

    /// <summary>A price or index value with thousands separators; small values (some coins) keep a few decimals.</summary>
    public static string Value(decimal value) => Math.Abs(value) switch
    {
        >= 100 => value.ToString("#,0", CultureInfo.InvariantCulture),
        >= 1 => value.ToString("#,0.##", CultureInfo.InvariantCulture),
        _ => value.ToString("0.######", CultureInfo.InvariantCulture),
    };

    /// <summary>A change in percent with its sign, e.g. «+0.8%» or «−0.3%» (a real minus sign, not a hyphen).</summary>
    public static string Change(decimal percent) =>
        (percent > 0 ? "+" : percent < 0 ? "−" : "") + Math.Abs(percent).ToString("0.0#", CultureInfo.InvariantCulture) + "%";

    /// <summary>How often a sync job runs, e.g. «هر ۱۵ دقیقه» or «ساعتی».</summary>
    public static string Interval(TimeSpan interval) => interval switch
    {
        { TotalDays: >= 1 } when interval.TotalDays % 1 == 0 => interval.TotalDays == 1 ? "روزانه" : $"هر {Digits((int)interval.TotalDays)} روز",
        { TotalHours: >= 1 } when interval.TotalHours % 1 == 0 => interval.TotalHours == 1 ? "ساعتی" : $"هر {Digits((int)interval.TotalHours)} ساعت",
        _ => $"هر {Digits((int)Math.Round(interval.TotalMinutes))} دقیقه",
    };

    /// <summary>Trading days, e.g. «شنبه تا چهارشنبه» for a run of days, otherwise a list.</summary>
    public static string WeekDays(IReadOnlyList<DayOfWeek> days)
    {
        if (days.Count == 0)
            return "همهٔ روزها";
        // Weeks start on Saturday in Iran.
        var order = days.Select(d => ((int)d + 1) % 7).Order().ToList();
        var isRun = order.Zip(order.Skip(1)).All(p => p.Second == p.First + 1);
        string Name(int i) => Days[(i + 6) % 7];
        return isRun && order.Count > 2 ? $"{Name(order[0])} تا {Name(order[^1])}" : string.Join("، ", order.Select(Name));
    }

    /// <summary>A time of day, e.g. «۸:۴۵».</summary>
    public static string Time(TimeOnly time) => Digits(time.ToString("H:mm", CultureInfo.InvariantCulture));
}
