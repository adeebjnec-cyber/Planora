using System.Globalization;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Reports;

/// <summary>
/// Compare Projects: the differences between two versions of a plan. Tasks and resources are matched by
/// unique ID (then by name), and changes to dates, durations, work, cost, progress, links and assignments
/// are listed, as in Microsoft Project's comparison report.
/// </summary>
public static class ProjectComparison
{
    public static Report Compare(Project current, Project previous, string previousName)
    {
        ProjectScheduler.Schedule(current);
        ProjectScheduler.Schedule(previous);
        var o = current.Options;
        var c = CultureInfo.CurrentCulture;
        string Date(DateTime d) => d.ToString("ddd d MMM yyyy", c);
        string Dur(long m) => DurationText.Format(m, o);
        string Work(long m) => DurationText.Format(m, o, DurationUnit.Hours);
        string Money(decimal m) => m.ToString("C", c);

        var matches = MatchTasks(current, previous);
        var added = current.Tasks.Where(t => !matches.ContainsKey(t)).ToList();
        var removed = previous.Tasks.Where(t => !matches.ContainsValue(t)).ToList();

        var changes = new List<IReadOnlyList<string>>();
        foreach (var (now, before) in matches)
        {
            void Diff(string field, string oldValue, string newValue)
            {
                if (oldValue != newValue)
                    changes.Add(new[] { $"{current.IndexOf(now) + 1}. {now.Name}", field, oldValue, newValue });
            }
            Diff("Name", before.Name, now.Name);
            Diff("Start", Date(before.Start), Date(now.Start));
            Diff("Finish", Date(before.Finish), Date(now.Finish));
            if (!now.IsSummary)
            {
                Diff("Duration", Dur(before.DurationMinutes), Dur(now.DurationMinutes));
                Diff("% Complete", $"{before.PercentComplete:0.#}%", $"{now.PercentComplete:0.#}%");
                Diff("Predecessors", PredecessorText.Format(previous, before), PredecessorText.Format(current, now));
                Diff("Resources", ResourceNamesText.Format(before), ResourceNamesText.Format(now));
            }
            Diff("Work", Work(before.WorkMinutes), Work(now.WorkMinutes));
            Diff("Cost", Money(before.Cost), Money(now.Cost));
        }

        var resourceChanges = new List<IReadOnlyList<string>>();
        foreach (var now in current.Resources)
        {
            var before = previous.GetResource(now.Id) is { } byId && byId.Name.Equals(now.Name, StringComparison.OrdinalIgnoreCase)
                ? byId
                : previous.Resources.FirstOrDefault(r => r.Name.Equals(now.Name, StringComparison.OrdinalIgnoreCase));
            if (before is null)
            {
                resourceChanges.Add(new[] { now.Name, "Added", "", now.Type.ToString() });
                continue;
            }
            void Diff(string field, string oldValue, string newValue)
            {
                if (oldValue != newValue)
                    resourceChanges.Add(new[] { now.Name, field, oldValue, newValue });
            }
            Diff("Max Units", $"{before.MaxUnits * 100:0}%", $"{now.MaxUnits * 100:0}%");
            Diff("Standard Rate", before.StandardRate.ToString("0.##", c), now.StandardRate.ToString("0.##", c));
            Diff("Work", Work(before.WorkMinutes), Work(now.WorkMinutes));
            Diff("Cost", Money(before.Cost), Money(now.Cost));
        }
        foreach (var gone in previous.Resources.Where(r => !current.Resources.Any(n => n.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase))))
            resourceChanges.Add(new[] { gone.Name, "Removed", gone.Type.ToString(), "" });

        var blocks = new List<ReportBlock>
        {
            new FiguresBlock(new[]
            {
                ("Finish (this version)", Date(current.Finish)),
                ($"Finish ({previousName})", Date(previous.Finish)),
                ("Finish change", Dur(current.Calendar.WorkingMinutesBetween(previous.Finish, current.Finish))),
                ("Cost change", Money(current.Cost - previous.Cost)),
                ("Work change", Work(current.WorkMinutes - previous.WorkMinutes)),
                ("Tasks added / removed / changed", $"{added.Count} / {removed.Count} / {changes.Select(r => r[0]).Distinct().Count()}"),
            }),
            new HeadingBlock("Changed tasks"),
            changes.Count == 0 ? new ParagraphBlock("No task fields changed.") : new TableBlock(new[] { "Task", "Field", previousName, "This version" }, changes),
            new HeadingBlock("Added tasks"),
            added.Count == 0 ? new ParagraphBlock("No tasks were added.") : new TableBlock(new[] { "Task", "Start", "Finish", "Duration" },
                added.Select(t => (IReadOnlyList<string>)new[] { $"{current.IndexOf(t) + 1}. {t.Name}", Date(t.Start), Date(t.Finish), Dur(t.DurationMinutes) }).ToList()),
            new HeadingBlock("Removed tasks"),
            removed.Count == 0 ? new ParagraphBlock("No tasks were removed.") : new TableBlock(new[] { "Task", "Start", "Finish", "Duration" },
                removed.Select(t => (IReadOnlyList<string>)new[] { t.Name, Date(t.Start), Date(t.Finish), Dur(t.DurationMinutes) }).ToList(),
                removed.Select(_ => true).ToList()),
            new HeadingBlock("Resources"),
            resourceChanges.Count == 0 ? new ParagraphBlock("No resource changes.") : new TableBlock(new[] { "Resource", "Field", previousName, "This version" }, resourceChanges),
        };
        return new Report("Compare Projects", $"{current.Name} compared with {previousName}  ·  {DateTime.Now.ToString("D", c)}", blocks);
    }

    /// <summary>Matches each current task to a previous one: same unique ID and name, else same name.</summary>
    public static Dictionary<ProjectTask, ProjectTask> MatchTasks(Project current, Project previous)
    {
        var matches = new Dictionary<ProjectTask, ProjectTask>();
        var used = new HashSet<ProjectTask>();
        foreach (var task in current.Tasks)
        {
            if (previous.GetTask(task.Id) is { } byId && byId.Name.Equals(task.Name, StringComparison.CurrentCultureIgnoreCase) && used.Add(byId))
                matches[task] = byId;
        }
        foreach (var task in current.Tasks.Where(t => !matches.ContainsKey(t)))
        {
            var byName = previous.Tasks.FirstOrDefault(t => !used.Contains(t) && t.Name.Equals(task.Name, StringComparison.CurrentCultureIgnoreCase));
            if (byName is null)
                byName = previous.GetTask(task.Id) is { } sameId && !used.Contains(sameId) ? sameId : null;
            if (byName is not null && used.Add(byName))
                matches[task] = byName;
        }
        return matches;
    }
}
