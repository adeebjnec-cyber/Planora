using System.Globalization;
using System.Text.RegularExpressions;

namespace Planora.Engine.Calendars;

/// <summary>Working hours as text: "8:00-12:00, 13:00-17:00", or empty / "Nonworking" for none.</summary>
public static partial class WorkingTimeText
{
    public const string NonWorking = "Nonworking";

    [GeneratedRegex(@"^\s*(?<from>\d{1,2}(:\d{2})?)\s*-\s*(?<to>\d{1,2}(:\d{2})?)\s*$")]
    private static partial Regex RangePattern();

    public static string Format(IReadOnlyList<WorkingTime> times) =>
        times.Count == 0 ? NonWorking : string.Join(", ", times.Select(t => t.ToString()));

    public static IReadOnlyList<WorkingTime> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals(NonWorking, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<WorkingTime>();

        var times = new List<WorkingTime>();
        foreach (var part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = RangePattern().Match(part);
            if (!match.Success)
                throw new FormatException($"'{part.Trim()}' is not a time range. Use e.g. 8:00-12:00.");
            try
            {
                times.Add(new WorkingTime(ParseTime(match.Groups["from"].Value), ParseTime(match.Groups["to"].Value)));
            }
            catch (ArgumentException ex)
            {
                throw new FormatException(ex.Message, ex);
            }
        }
        try
        {
            return WorkingTime.Normalize(times);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }

    private static TimeSpan ParseTime(string text)
    {
        var parts = text.Split(':');
        int hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int minutes = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        if (hours > 24 || minutes > 59 || hours == 24 && minutes > 0)
            throw new FormatException($"'{text}' is not a valid time.");
        return new TimeSpan(hours, minutes, 0);
    }
}
