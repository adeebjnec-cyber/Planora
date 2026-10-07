using System.Globalization;
using Planora.Engine.Calendars;

namespace Planora.App.Infrastructure;

/// <summary>Dates as shown and typed in Planora, e.g. "28 Sep 2026".</summary>
public static class DateText
{
    /// <summary>The user's short date format with a two-digit year.</summary>
    private static readonly string ShortDatePattern =
        CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern.Replace("yyyy", "yy");

    /// <summary>Formats a date ("28 Sep 2026"); the time is shown only when it is not a usual start/end of day.</summary>
    public static string Format(DateTime date) =>
        date.ToString("d MMM yyyy", CultureInfo.CurrentCulture) +
        (date.TimeOfDay != TimeSpan.FromHours(8) && date.TimeOfDay != TimeSpan.FromHours(17) ? date.ToString(" t", CultureInfo.CurrentCulture) : "");

    public static string Format(DateTime? date) => date is { } d ? Format(d) : "";

    /// <summary>Plain date for date-only fields (holidays, exceptions).</summary>
    public static string FormatDay(DateOnly date) => date.ToString(ShortDatePattern, CultureInfo.CurrentCulture);

    /// <summary>
    /// Parses a typed date. A date without a time means the start of that day's working time
    /// (or its end, for finish dates), on the given calendar.
    /// </summary>
    public static bool TryParse(string? text, WorkingCalendar calendar, bool endOfDay, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var cleaned = text.Trim();
        // Accept the displayed format, which starts with a day name.
        int space = cleaned.IndexOf(' ');
        if (space > 0 && !char.IsDigit(cleaned[0]))
            cleaned = cleaned[(space + 1)..];

        if (!DateTime.TryParse(cleaned, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return false;

        if (date.TimeOfDay == TimeSpan.Zero)
            date = endOfDay ? calendar.PreviousWorkingMoment(date.AddDays(1)) : calendar.NextWorkingMoment(date);
        return true;
    }

    public static bool TryParseDay(string? text, out DateOnly day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(text) ||
            !DateTime.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
            return false;
        day = DateOnly.FromDateTime(date);
        return true;
    }
}
