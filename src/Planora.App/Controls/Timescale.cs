using System.Globalization;
using Planora.Engine.Views;

namespace Planora.App.Controls;

public enum TimeUnit
{
    Hour,
    Day,
    Week,
    ThirdOfMonth,
    Month,
    Quarter,
    HalfYear,
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
            TimeUnit.ThirdOfMonth => new DateTime(date.Year, date.Month, date.Day < 11 ? 1 : date.Day < 21 ? 11 : 21),
            TimeUnit.Month => new DateTime(date.Year, date.Month, 1),
            TimeUnit.Quarter => new DateTime(date.Year, (date.Month - 1) / 3 * 3 + 1, 1),
            TimeUnit.HalfYear => new DateTime(date.Year, date.Month <= 6 ? 1 : 7, 1),
            _ => new DateTime(date.Year, 1, 1),
        };
    }

    public DateTime Next(DateTime boundary) => Unit switch
    {
        TimeUnit.Hour => boundary.AddHours(Step),
        TimeUnit.Day => boundary.AddDays(Step),
        TimeUnit.Week => boundary.AddDays(7 * Step),
        TimeUnit.ThirdOfMonth => Enumerable.Range(0, Step).Aggregate(boundary, (b, _) => b.Day switch
        {
            1 => b.AddDays(10),
            11 => b.AddDays(10),
            _ => new DateTime(b.Year, b.Month, 1).AddMonths(1),
        }),
        TimeUnit.Month => boundary.AddMonths(Step),
        TimeUnit.Quarter => boundary.AddMonths(3 * Step),
        TimeUnit.HalfYear => boundary.AddMonths(6 * Step),
        _ => boundary.AddYears(Step),
    };

    /// <summary>Tier boundaries covering [from, to].</summary>
    public IEnumerable<DateTime> Boundaries(DateTime from, DateTime to)
    {
        for (var b = Floor(from); b <= to; b = Next(b))
            yield return b;
    }
}

/// <summary>
/// Picks the timescale tiers (like Microsoft Project's top/middle/bottom tiers): automatically for a zoom
/// level, or from the project's Timescale settings.
/// </summary>
public static class Timescale
{
    public static DayOfWeek FirstDayOfWeek => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    private static string Fmt(DateTime d, string format) => d.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The tiers to draw, top first: the settings' fixed tiers, or two automatic ones.</summary>
    public static IReadOnlyList<TimescaleTier> Tiers(double pixelsPerDay, TimescaleSettings? settings)
    {
        if (settings is { Automatic: false, Tiers.Count: > 0 })
        {
            return settings.Tiers.Take(Math.Clamp(settings.TierCount, 1, Math.Min(3, settings.Tiers.Count)))
                .Select(t => new TimescaleTier(Map(t.Unit), Math.Max(1, t.Count), t.Format))
                .ToList();
        }
        var (top, bottom) = For(pixelsPerDay);
        // Day-level zooms read month, then week, then day.
        if (pixelsPerDay is >= 9 and < 100)
            return new[] { new TimescaleTier(TimeUnit.Month, 1, d => Fmt(d, "MMMM yyyy")), top, bottom };
        return new[] { top, bottom };
    }

    /// <summary>"Week 41", with the week's dates when there is room ("Week 41  5 Oct – 11 Oct").</summary>
    private static string WeekLabel(DateTime start, double pixelsPerDay)
    {
        string week = "Week " + ISOWeek.GetWeekOfYear(start).ToString(CultureInfo.CurrentCulture);
        return pixelsPerDay * 7 >= 190 ? $"{week}   {Fmt(start, "d MMM")} – {Fmt(start.AddDays(6), "d MMM")}" : week;
    }

    private static TimeUnit Map(TimescaleUnit unit) => unit switch
    {
        TimescaleUnit.Hours => TimeUnit.Hour,
        TimescaleUnit.Days => TimeUnit.Day,
        TimescaleUnit.Weeks => TimeUnit.Week,
        TimescaleUnit.ThirdsOfMonths => TimeUnit.ThirdOfMonth,
        TimescaleUnit.Months => TimeUnit.Month,
        TimescaleUnit.Quarters => TimeUnit.Quarter,
        TimescaleUnit.HalfYears => TimeUnit.HalfYear,
        _ => TimeUnit.Year,
    };

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
                : pixelsPerDay >= 16 ? d => Fmt(d, "%d")
                : d => Fmt(d, "ddd")[..1];
            return (new TimescaleTier(TimeUnit.Week, 1, d => WeekLabel(d, pixelsPerDay)),
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
