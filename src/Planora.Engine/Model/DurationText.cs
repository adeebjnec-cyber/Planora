using System.Globalization;
using System.Text.RegularExpressions;

namespace Planora.Engine.Model;

public enum DurationUnit
{
    Minutes,
    Hours,
    Days,
    Weeks,
    Months,
}

/// <summary>A duration as typed by the user: e.g. "3d", "2 wks", "5ed" (elapsed), "4h?" (estimated).</summary>
public readonly record struct ParsedDuration(long Minutes, DurationUnit Unit, bool IsElapsed, bool IsEstimated);

/// <summary>A lag as typed by the user: e.g. "2d", "-1d" (lead), "3ed" (elapsed), "50%".</summary>
public readonly record struct ParsedLag(LagFormat Format, long Minutes, double Percent);

/// <summary>Parses and formats durations the way Microsoft Project does, using the project's hours-per-day settings.</summary>
public static partial class DurationText
{
    private const int ElapsedDay = 24 * 60;

    [GeneratedRegex(@"^\s*(?<amount>[+-]?\d+(?:[.,]\d+)?)\s*(?<unit>[a-z]*)\s*(?<estimated>\?)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"^\s*(?<amount>[+-]?\d+(?:[.,]\d+)?)\s*%\s*$")]
    private static partial Regex PercentPattern();

    public static bool TryParse(string? text, ScheduleOptions options, out ParsedDuration duration, DurationUnit defaultUnit = DurationUnit.Days)
    {
        duration = default;
        if (!TryParseSigned(text, options, defaultUnit, out var parsed) || parsed.Minutes < 0)
            return false;
        duration = parsed;
        return true;
    }

    public static ParsedDuration Parse(string text, ScheduleOptions options, DurationUnit defaultUnit = DurationUnit.Days) =>
        TryParse(text, options, out var d, defaultUnit) ? d : throw new FormatException($"'{text}' is not a valid duration.");

    public static bool TryParseLag(string? text, ScheduleOptions options, out ParsedLag lag)
    {
        lag = default;
        if (text is null)
            return false;

        var percent = PercentPattern().Match(text);
        if (percent.Success)
        {
            lag = new ParsedLag(LagFormat.PercentOfPredecessor, 0, ParseNumber(percent.Groups["amount"].Value));
            return true;
        }

        if (!TryParseSigned(text, options, DurationUnit.Days, out var parsed) || parsed.IsEstimated)
            return false;
        lag = new ParsedLag(parsed.IsElapsed ? LagFormat.ElapsedTime : LagFormat.WorkingTime, parsed.Minutes, 0);
        return true;
    }

    public static string Format(long minutes, ScheduleOptions options, DurationUnit unit = DurationUnit.Days, bool isElapsed = false, bool isEstimated = false)
    {
        double amount = minutes / (double)UnitMinutes(unit, isElapsed, options);
        string number = amount.ToString("0.##", CultureInfo.CurrentCulture);
        string name = (isElapsed ? "e" : "") + unit switch
        {
            DurationUnit.Minutes => "min",
            DurationUnit.Hours => "hr",
            DurationUnit.Days => "day",
            DurationUnit.Weeks => "wk",
            _ => "mon",
        };
        if (Math.Abs(amount) != 1)
            name += "s";
        return $"{number} {name}{(isEstimated ? "?" : "")}";
    }

    public static string FormatLag(TaskLink link, ScheduleOptions options, DurationUnit unit = DurationUnit.Days) =>
        link.LagFormat == LagFormat.PercentOfPredecessor
            ? link.LagPercent.ToString("0.##", CultureInfo.CurrentCulture) + "%"
            : Format(link.LagMinutes, options, unit, link.LagFormat == LagFormat.ElapsedTime);

    public static long UnitMinutes(DurationUnit unit, bool isElapsed, ScheduleOptions options) => unit switch
    {
        DurationUnit.Minutes => 1,
        DurationUnit.Hours => 60,
        DurationUnit.Days => isElapsed ? ElapsedDay : options.MinutesPerDay,
        DurationUnit.Weeks => isElapsed ? 7 * ElapsedDay : options.MinutesPerWeek,
        _ => isElapsed ? 30 * ElapsedDay : (long)options.DaysPerMonth * options.MinutesPerDay,
    };

    private static bool TryParseSigned(string? text, ScheduleOptions options, DurationUnit defaultUnit, out ParsedDuration duration)
    {
        duration = default;
        if (text is null)
            return false;
        var match = DurationPattern().Match(text);
        if (!match.Success)
            return false;

        var unitText = match.Groups["unit"].Value.ToLowerInvariant();
        // No working-time unit starts with "e", so a leading "e" always means elapsed ("ed", "emo", "em").
        bool elapsed = unitText.Length > 1 && unitText[0] == 'e';
        if (elapsed)
            unitText = unitText[1..];

        DurationUnit? unit = unitText switch
        {
            "" => defaultUnit,
            "m" or "min" or "mins" or "minute" or "minutes" => DurationUnit.Minutes,
            "h" or "hr" or "hrs" or "hour" or "hours" => DurationUnit.Hours,
            "d" or "dy" or "day" or "days" => DurationUnit.Days,
            "w" or "wk" or "wks" or "week" or "weeks" => DurationUnit.Weeks,
            "mo" or "mon" or "mons" or "month" or "months" => DurationUnit.Months,
            _ => null,
        };
        if (unit is null)
            return false;

        double amount = ParseNumber(match.Groups["amount"].Value);
        long minutes = (long)Math.Round(amount * UnitMinutes(unit.Value, elapsed, options));
        duration = new ParsedDuration(minutes, unit.Value, elapsed, match.Groups["estimated"].Success);
        return true;
    }

    private static double ParseNumber(string text) =>
        double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
}
