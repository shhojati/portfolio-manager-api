using System.Globalization;

namespace Chandoo.Site;

/// <summary>How the pages write numbers and dates in Persian.</summary>
public static class PersianFormat
{
    private static readonly PersianCalendar Calendar = new();

    private static readonly string[] Months =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

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
}
