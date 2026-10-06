using System.Globalization;

namespace Planora.App.Controls;

public enum TimeUnit
{
    Hour,
    Day,
    Week,
    Month,
    Quarter,
    Year,
}

/// <summary>One tier of the Gantt timescale: a unit, a step (e.g. every 6 hours) and a label format.</summary>
public sealed record TimescaleTier(TimeUnit Unit, int Step, Func<DateTime, string> Label)
{
    public DateTime Floor(DateTime date)
    {
        var day = date.Date;
        return Unit switch
        {
            TimeUnit.Hour => day.AddHours(Math.Floor(date.TimeOfDay.TotalHours / Step) * Step),
            TimeUnit.Day => day,
            TimeUnit.Week => day.AddDays(-(((int)day.DayOfWeek - (int)Timescale.FirstDayOfWeek + 7) % 7)),
            TimeUnit.Month => new DateTime(date.Year, date.Month, 1),
            TimeUnit.Quarter => new DateTime(date.Year, (date.Month - 1) / 3 * 3 + 1, 1),
            _ => new DateTime(date.Year, 1, 1),
        };
    }

    public DateTime Next(DateTime boundary) => Unit switch
    {
        TimeUnit.Hour => boundary.AddHours(Step),
        TimeUnit.Day => boundary.AddDays(Step),
        TimeUnit.Week => boundary.AddDays(7 * Step),
        TimeUnit.Month => boundary.AddMonths(Step),
        TimeUnit.Quarter => boundary.AddMonths(3 * Step),
        _ => boundary.AddYears(Step),
    };

    /// <summary>Tier boundaries covering [from, to].</summary>
    public IEnumerable<DateTime> Boundaries(DateTime from, DateTime to)
    {
        for (var b = Floor(from); b <= to; b = Next(b))
            yield return b;
    }
}

/// <summary>Picks the two timescale tiers (like Microsoft Project's top/bottom tiers) for a zoom level.</summary>
public static class Timescale
{
    public static DayOfWeek FirstDayOfWeek => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    private static string Fmt(DateTime d, string format) => d.ToString(format, CultureInfo.CurrentCulture);

    public static (TimescaleTier Top, TimescaleTier Bottom) For(double pixelsPerDay)
    {
        if (pixelsPerDay >= 100)
        {
            double pixelsPerHour = pixelsPerDay / 24;
            int step = new[] { 1, 2, 3, 4, 6, 12 }.First(s => s * pixelsPerHour >= 28 || s == 12);
            return (new TimescaleTier(TimeUnit.Day, 1, d => Fmt(d, "ddd d MMM yyyy")),
                    new TimescaleTier(TimeUnit.Hour, step, d => Fmt(d, "HH")));
        }
        if (pixelsPerDay >= 9)
        {
            Func<DateTime, string> dayLabel = pixelsPerDay >= 40 ? d => Fmt(d, "ddd d")
                : pixelsPerDay >= 18 ? d => Fmt(d, "%d")
                : d => Fmt(d, "ddd")[..1];
            return (new TimescaleTier(TimeUnit.Week, 1, d => Fmt(d, "d MMM ''yy")),
                    new TimescaleTier(TimeUnit.Day, 1, dayLabel));
        }
        if (pixelsPerDay >= 3)
        {
            return (new TimescaleTier(TimeUnit.Month, 1, d => Fmt(d, "MMMM yyyy")),
                    new TimescaleTier(TimeUnit.Week, 1, d => Fmt(d, "%d")));
        }
        if (pixelsPerDay >= 1)
        {
            return (new TimescaleTier(TimeUnit.Quarter, 1, d => $"Q{(d.Month - 1) / 3 + 1} {d.Year}"),
                    new TimescaleTier(TimeUnit.Month, 1, d => Fmt(d, "MMM")));
        }
        return (new TimescaleTier(TimeUnit.Year, 1, d => d.Year.ToString(CultureInfo.CurrentCulture)),
                new TimescaleTier(TimeUnit.Quarter, 1, d => $"Q{(d.Month - 1) / 3 + 1}"));
    }
}
