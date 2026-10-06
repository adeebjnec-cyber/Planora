using System.Text.RegularExpressions;

namespace Planora.Engine.Model;

/// <summary>
/// The "Predecessors" column text, as in Microsoft Project: comma-separated entries of
/// row number, optional link type and optional lag, e.g. "3, 5SS+2d, 7FF-50%".
/// </summary>
public static partial class PredecessorText
{
    [GeneratedRegex(@"^\s*(?<row>\d+)\s*(?<type>FS|SS|FF|SF)?\s*(?<lag>[+-].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex EntryPattern();

    public static string Format(Project project, ProjectTask task, DurationUnit unit = DurationUnit.Days)
    {
        var parts = new List<string>();
        foreach (var link in task.Predecessors)
        {
            var predecessor = project.GetTask(link.PredecessorId);
            if (predecessor is null)
                continue;
            string text = (project.IndexOf(predecessor) + 1).ToString();
            bool hasLag = link.LagFormat == LagFormat.PercentOfPredecessor ? link.LagPercent != 0 : link.LagMinutes != 0;
            if (link.Type != LinkType.FinishToStart || hasLag)
                text += Abbreviation(link.Type);
            if (hasLag)
            {
                var lag = DurationText.FormatLag(link, project.Options, unit);
                text += lag.StartsWith('-') ? lag : "+" + lag;
            }
            parts.Add(text);
        }
        return string.Join(",", parts);
    }

    /// <summary>
    /// Replaces the task's predecessors with those described by <paramref name="text"/>.
    /// On any error the existing links are kept and a <see cref="FormatException"/> or
    /// <see cref="InvalidOperationException"/> explains the problem.
    /// </summary>
    public static void Apply(Project project, ProjectTask task, string? text)
    {
        var parsed = new List<(ProjectTask Predecessor, LinkType Type, ParsedLag Lag)>();
        foreach (var entry in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = EntryPattern().Match(entry);
            if (!match.Success)
                throw new FormatException($"'{entry}' is not a valid predecessor. Use a row number with an optional type and lag, e.g. 3FS+2d.");

            int row = int.Parse(match.Groups["row"].Value);
            if (row < 1 || row > project.Tasks.Count)
                throw new FormatException($"There is no task in row {row}.");

            var type = match.Groups["type"].Success ? ParseType(match.Groups["type"].Value) : LinkType.FinishToStart;
            var lag = new ParsedLag(LagFormat.WorkingTime, 0, 0);
            if (match.Groups["lag"].Success && !DurationText.TryParseLag(match.Groups["lag"].Value.Trim(), project.Options, out lag))
                throw new FormatException($"'{match.Groups["lag"].Value.Trim()}' is not a valid lag.");

            parsed.Add((project.Tasks[row - 1], type, lag));
        }

        var previous = task.Predecessors.ToList();
        task.Predecessors.Clear();
        try
        {
            foreach (var (predecessor, type, lag) in parsed)
            {
                var link = project.Link(predecessor, task, type, lag.Minutes);
                link.LagFormat = lag.Format;
                link.LagPercent = lag.Percent;
            }
        }
        catch
        {
            task.Predecessors.Clear();
            task.Predecessors.AddRange(previous);
            throw;
        }
    }

    public static string Abbreviation(LinkType type) => type switch
    {
        LinkType.StartToStart => "SS",
        LinkType.FinishToFinish => "FF",
        LinkType.StartToFinish => "SF",
        _ => "FS",
    };

    private static LinkType ParseType(string text) => text.ToUpperInvariant() switch
    {
        "SS" => LinkType.StartToStart,
        "FF" => LinkType.FinishToFinish,
        "SF" => LinkType.StartToFinish,
        _ => LinkType.FinishToStart,
    };
}
