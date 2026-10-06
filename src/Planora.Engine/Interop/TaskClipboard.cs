using System.Globalization;
using System.Text;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Interop;

/// <summary>
/// Copy and paste of tasks. Copied tasks (with their subtasks) are stored as a small project in the
/// native file format, so pasting keeps outline structure, links among the copied tasks, constraints,
/// notes, custom values and assignments (resources are matched by name, or created).
/// </summary>
public static class TaskClipboard
{
    /// <summary>Clipboard format name used by the application.</summary>
    public const string Format = "Planora.Tasks.v1";

    /// <summary>Serialises the given tasks and all their subtasks.</summary>
    public static string Copy(Project project, IEnumerable<ProjectTask> tasks)
    {
        var selected = tasks.SelectMany(project.Subtree).Distinct().OrderBy(project.IndexOf).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("Select tasks to copy.");

        // A copy of the project trimmed to the selected tasks keeps every field and link intact.
        var copy = ProjectFile.Deserialize(ProjectFile.Serialize(project));
        var keep = selected.Select(t => t.Id).ToHashSet();

        // Drop unrelated tasks, then summaries that only sit above selected tasks (keeping those tasks).
        foreach (var task in copy.Tasks.Where(t => !keep.Contains(t.Id) && !copy.Subtree(t).Any(d => keep.Contains(d.Id))).ToList())
        {
            if (copy.Tasks.Contains(task))
                copy.RemoveTask(task);
        }
        foreach (var task in copy.Tasks.Where(t => !keep.Contains(t.Id)).ToList())
            copy.RemoveTaskKeepChildren(task);

        // The shallowest copied task becomes level 1.
        int shift = copy.Tasks.Min(t => t.OutlineLevel) - 1;
        foreach (var task in copy.Tasks)
            task.OutlineLevel -= shift;
        copy.RebuildHierarchy();
        foreach (var resource in copy.Resources.Where(r => !copy.Assignments.Any(a => a.Resource == r)).ToList())
            copy.RemoveResource(resource);
        return ProjectFile.Serialize(copy);
    }

    /// <summary>Tab-separated text (name, duration, start, finish) for pasting into spreadsheets.</summary>
    public static string CopyAsText(Project project, IEnumerable<ProjectTask> tasks)
    {
        var sb = new StringBuilder();
        foreach (var task in tasks.SelectMany(project.Subtree).Distinct().OrderBy(project.IndexOf))
        {
            sb.Append(new string(' ', (task.OutlineLevel - 1) * 2)).Append(task.Name).Append('\t')
              .Append(DurationText.Format(task.DurationMinutes, project.Options, isElapsed: task.IsElapsedDuration)).Append('\t')
              .Append(task.Start.ToString("g", CultureInfo.CurrentCulture)).Append('\t')
              .Append(task.Finish.ToString("g", CultureInfo.CurrentCulture)).AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Inserts copied tasks before row <paramref name="index"/>, at that row's outline level.
    /// Returns the new tasks.
    /// </summary>
    public static List<ProjectTask> Paste(Project project, string data, int index)
    {
        var source = ProjectFile.Deserialize(data);
        ProjectScheduler.Schedule(source);
        index = Math.Clamp(index, 0, project.Tasks.Count);
        int baseLevel = index < project.Tasks.Count ? project.Tasks[index].OutlineLevel
            : index > 0 ? project.Tasks[index - 1].OutlineLevel : 1;

        var map = new Dictionary<int, ProjectTask>();
        var created = new List<ProjectTask>();
        foreach (var original in source.Tasks)
        {
            var task = project.InsertTask(index + created.Count, original.Name);
            int wantedLevel = baseLevel + original.OutlineLevel - 1;
            while (task.OutlineLevel < wantedLevel && project.IndexOf(task) > 0 && project.Tasks[project.IndexOf(task) - 1].OutlineLevel >= task.OutlineLevel)
                project.Indent(task);
            while (task.OutlineLevel > wantedLevel)
                project.Outdent(task);
            CopyFields(original, task, project);
            map[original.Id] = task;
            created.Add(task);
        }

        foreach (var original in source.Tasks)
        {
            var successor = map[original.Id];
            foreach (var link in original.Predecessors)
            {
                if (!map.TryGetValue(link.PredecessorId, out var predecessor))
                    continue;
                try
                {
                    var copy = project.Link(predecessor, successor, link.Type, link.LagMinutes);
                    copy.LagFormat = link.LagFormat;
                    copy.LagPercent = link.LagPercent;
                }
                catch (InvalidOperationException)
                {
                    // The pasted structure may make a link invalid (e.g. summary to own subtask); skip it.
                }
            }
        }

        foreach (var original in source.Tasks.Where(t => !t.IsSummary))
        {
            var task = map[original.Id];
            if (task.IsSummary)
                continue;
            foreach (var a in original.Assignments)
            {
                var resource = project.Resources.FirstOrDefault(r => r.Name.Equals(a.Resource.Name, StringComparison.OrdinalIgnoreCase) && r.Type == a.Resource.Type)
                    ?? CopyResource(project, a.Resource);
                if (resource.IsBudget || task.Assignments.Any(x => x.Resource == resource))
                    continue;
                var assignment = Project.RestoreAssignment(task, resource, a.Units);
                assignment.WorkMinutes = a.WorkMinutes;
                assignment.CostAmount = a.CostAmount;
                assignment.Contour = a.Contour;
                assignment.CostRateTable = a.CostRateTable;
                assignment.DelayMinutes = a.DelayMinutes;
                assignment.LevelingDelayMinutes = a.LevelingDelayMinutes;
            }
        }
        return created;
    }

    /// <summary>Creates tasks from plain text: one task per line, indented lines become subtasks, an optional tab-separated duration.</summary>
    public static List<ProjectTask> PasteText(Project project, string text, int index)
    {
        var created = new List<ProjectTask>();
        index = Math.Clamp(index, 0, project.Tasks.Count);
        int baseLevel = index < project.Tasks.Count ? project.Tasks[index].OutlineLevel : index > 0 ? project.Tasks[index - 1].OutlineLevel : 1;
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0))
        {
            var cells = line.Split('\t');
            int indent = cells[0].TakeWhile(c => c is ' ').Count() / 2 + cells[0].TakeWhile(c => c is ' ').Count();
            var task = project.InsertTask(index + created.Count, cells[0].Trim());
            int wantedLevel = baseLevel + indent;
            while (task.OutlineLevel < wantedLevel && project.IndexOf(task) > 0 && project.Tasks[project.IndexOf(task) - 1].OutlineLevel >= task.OutlineLevel)
                project.Indent(task);
            while (task.OutlineLevel > wantedLevel)
                project.Outdent(task);
            if (cells.Length > 1 && DurationText.TryParse(cells[1], project.Options, out var d))
            {
                task.DurationMinutes = d.Minutes;
                task.IsElapsedDuration = d.IsElapsed;
                task.IsEstimated = d.IsEstimated;
            }
            else
            {
                task.DurationMinutes = project.Options.MinutesPerDay;
                task.IsEstimated = true;
            }
            created.Add(task);
        }
        return created;
    }

    private static void CopyFields(ProjectTask from, ProjectTask to, Project target)
    {
        to.Notes = from.Notes;
        to.IsActive = from.IsActive;
        to.Mode = from.Mode;
        to.DurationMinutes = from.IsSummary ? 0 : from.DurationMinutes;
        to.IsElapsedDuration = from.IsElapsedDuration;
        to.IsEstimated = from.IsEstimated;
        to.IsMilestone = from.IsMilestone;
        to.ShowOnTimeline = from.ShowOnTimeline;
        to.ConstraintType = from.ConstraintType;
        to.ConstraintDate = from.ConstraintDate;
        to.Deadline = from.Deadline;
        to.Calendar = from.Calendar is { } cal ? target.Calendars.FirstOrDefault(c => c.Name == cal.Name) : null;
        to.ManualStart = from.ManualStart;
        to.ManualFinish = from.ManualFinish;
        to.Type = from.Type;
        to.IsEffortDriven = from.IsEffortDriven;
        to.IgnoreResourceCalendar = from.IgnoreResourceCalendar;
        to.Hyperlink = from.Hyperlink;
        to.HyperlinkAddress = from.HyperlinkAddress;
        to.HyperlinkSubAddress = from.HyperlinkSubAddress;
        to.IsMarked = from.IsMarked;
        to.PhysicalPercentComplete = from.PhysicalPercentComplete;
        to.EarnedValueMethod = from.EarnedValueMethod;
        to.HideBar = from.HideBar;
        to.FixedCost = from.FixedCost;
        to.FixedCostAccrual = from.FixedCostAccrual;
        to.Priority = from.Priority;
        foreach (var split in from.Splits)
            to.Split(split.AfterWorkMinutes, split.GapMinutes);
        foreach (var (key, value) in from.CustomValues)
            to.CustomValues[key] = value;
    }

    private static Resource CopyResource(Project project, Resource source)
    {
        var resource = project.AddResource(source.Name, source.Type);
        resource.Initials = source.Initials;
        resource.Group = source.Group;
        resource.MaterialLabel = source.MaterialLabel;
        resource.MaxUnits = source.MaxUnits;
        resource.AccrueAt = source.AccrueAt;
        for (int i = 0; i < Resource.RateTableCount; i++)
        {
            resource.CostRateTables[i].Clear();
            resource.CostRateTables[i].AddRange(source.CostRateTables[i]);
        }
        return resource;
    }
}
