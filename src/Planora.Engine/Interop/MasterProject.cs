using Planora.Engine.Calendars;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Interop;

/// <summary>Opens a project file by extension: Planora, Microsoft Project XML, or (through MPXJ) .mpp and other formats.</summary>
public static class ProjectLoader
{
    public static Project Load(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".planora" => ProjectFile.Load(path),
        ".xml" => MspdiFile.Load(path),
        _ => ForeignFormats.Load(path),
    };
}

/// <summary>
/// Master projects and subprojects, as in Microsoft Project's Insert Project: another project file is
/// brought in as a summary task whose subtasks are that project's tasks. The summary remembers the file,
/// so the subproject can be refreshed from it or its changes saved back to it. Links between tasks of
/// different subprojects are ordinary links in the master project.
/// </summary>
public static class MasterProject
{
    /// <summary>Inserts a project file before row <paramref name="index"/>. Returns the subproject's summary task.</summary>
    public static ProjectTask InsertSubproject(Project master, string path, int index)
    {
        var source = ProjectLoader.Load(path);
        var summary = master.InsertTask(Math.Clamp(index, 0, master.Tasks.Count), source.Name);
        summary.SubprojectPath = path;
        FillFrom(master, summary, source);
        return summary;
    }

    /// <summary>Replaces the subproject's tasks with the current contents of its file.</summary>
    public static void RefreshSubproject(Project master, ProjectTask summary)
    {
        if (!summary.IsSubproject)
            throw new InvalidOperationException($"'{summary.Name}' is not an inserted project.");
        var source = ProjectLoader.Load(summary.SubprojectPath!);
        foreach (var child in summary.Children.ToList())
            master.RemoveTask(child);
        FillFrom(master, summary, source);
    }

    /// <summary>Writes the subproject's tasks (as edited in the master) back to its file.</summary>
    public static void SaveSubproject(Project master, ProjectTask summary)
    {
        if (!summary.IsSubproject)
            throw new InvalidOperationException($"'{summary.Name}' is not an inserted project.");
        var path = summary.SubprojectPath!;
        var target = new Project(summary.Name, summary.Children.Count > 0 ? summary.Start : master.StartDate);
        if (summary.Children.Count > 0)
            TaskClipboard.Paste(target, TaskClipboard.Copy(master, summary.Children), 0);
        ProjectScheduler.Schedule(target);
        if (Path.GetExtension(path).Equals(MspdiFile.Extension, StringComparison.OrdinalIgnoreCase))
            MspdiFile.Save(target, path);
        else
            ProjectFile.Save(target, path);
    }

    /// <summary>Copies a source project's tasks under the summary (as its subtasks).</summary>
    private static void FillFrom(Project master, ProjectTask summary, Project source)
    {
        if (source.Tasks.Count == 0)
            return;
        var roots = source.Tasks.Where(t => t.Parent is null).ToList();
        var pasted = TaskClipboard.Paste(master, TaskClipboard.Copy(source, roots), master.IndexOf(summary) + 1);
        // The pasted top-level tasks become the summary's children.
        int rootLevel = pasted.Min(t => t.OutlineLevel);
        foreach (var root in pasted.Where(t => t.OutlineLevel == rootLevel).ToList())
        {
            while (root.OutlineLevel <= summary.OutlineLevel)
                master.Indent(root);
        }
    }
}

/// <summary>
/// Shared resource pools, as in Microsoft Project's Resource Pool: several projects use the resources of
/// one pool file, so rates, availability and calendars are kept in one place.
/// </summary>
public static class ResourcePool
{
    /// <summary>
    /// Uses the pool's resources in this project (matching by name, adding the rest). Returns how many were
    /// added. When the project's own file is given and the pool is a Planora file, the pool records it as a
    /// sharer, so over-allocations can be checked across all sharing projects.
    /// </summary>
    public static int Share(Project project, string poolPath, string? projectPath = null)
    {
        var pool = ProjectLoader.Load(poolPath);
        project.ResourcePoolPath = poolPath;
        int added = Merge(from: pool, into: project);
        if (projectPath is not null && Path.GetExtension(poolPath).Equals(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var full = Path.GetFullPath(projectPath);
            if (!pool.PoolSharers.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                pool.PoolSharers.Add(full);
                ProjectFile.Save(pool, poolPath);
            }
        }
        return added;
    }

    /// <summary>The project files recorded as sharing a pool.</summary>
    public static IReadOnlyList<string> Sharers(string poolPath) => ProjectLoader.Load(poolPath).PoolSharers;

    /// <summary>
    /// Over-allocations of this project's pool resources once the work booked in the other sharing projects
    /// is added (checked day by day, matching resources by name). <paramref name="thisProjectPath"/> is
    /// skipped among the sharers, since this project's current (possibly unsaved) plan is used instead.
    /// </summary>
    public static IReadOnlyList<PoolOverallocation> SharedOverallocations(Project project, string? thisProjectPath = null)
    {
        var path = project.ResourcePoolPath ?? throw new InvalidOperationException("This project does not use a resource pool.");
        var others = new List<Project>();
        foreach (var sharer in Sharers(path))
        {
            if (thisProjectPath is not null && string.Equals(Path.GetFullPath(sharer), Path.GetFullPath(thisProjectPath), StringComparison.OrdinalIgnoreCase))
                continue;
            if (!File.Exists(sharer))
                continue;
            var other = ProjectLoader.Load(sharer);
            ProjectScheduler.Schedule(other);
            others.Add(other);
        }
        ProjectScheduler.Schedule(project);

        var result = new List<PoolOverallocation>();
        foreach (var resource in project.Resources.Where(r => r.Type == ResourceType.Work && !r.IsBudget))
        {
            var booked = new List<(Assignment Assignment, string Project)>();
            booked.AddRange(ResourceAnalysis.ScheduledAssignments(project, resource).Select(a => (a, project.Name)));
            foreach (var other in others)
            {
                if (other.Resources.FirstOrDefault(r => r.Name.Equals(resource.Name, StringComparison.OrdinalIgnoreCase)) is { } match)
                    booked.AddRange(ResourceAnalysis.ScheduledAssignments(other, match).Select(a => (a, other.Name)));
            }
            if (booked.Count == 0)
                continue;
            var calendar = resource.Calendar ?? project.Calendar;
            var first = booked.Min(b => b.Assignment.Start).Date;
            var last = booked.Max(b => b.Assignment.Finish).Date;
            PoolOverallocation? open = null;
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                var next = day.AddDays(1);
                double working = calendar.WorkingMinutesBetween(day, next);
                double available = working * resource.MaxUnitsAt(day);
                var today = booked.Where(b => b.Assignment.Start < next && b.Assignment.Finish > day).ToList();
                double load = today.Sum(b => Timephased.WorkBetween(b.Assignment, day, next));
                if (load > available + 0.5)
                {
                    // Load as a percentage of one full-time person's working day.
                    double percent = 100 * load / (working > 0 ? working : project.Options.MinutesPerDay);
                    var projects = today.Where(b => Timephased.WorkBetween(b.Assignment, day, next) > 0).Select(b => b.Project).Distinct().ToList();
                    if (open is not null && open.To == day)
                        open = open with { To = next, PeakPercent = Math.Max(open.PeakPercent, percent), Projects = open.Projects.Union(projects).ToList() };
                    else
                    {
                        if (open is not null)
                            result.Add(open);
                        open = new PoolOverallocation(resource.Name, day, next, percent, projects);
                    }
                }
            }
            if (open is not null)
                result.Add(open);
        }
        return result;
    }

    /// <summary>Brings the pool's latest resource details into this project.</summary>
    public static int Refresh(Project project)
    {
        var path = project.ResourcePoolPath ?? throw new InvalidOperationException("This project does not use a resource pool.");
        return Merge(from: ProjectLoader.Load(path), into: project);
    }

    /// <summary>Writes this project's resource details (and new resources) back into the pool file.</summary>
    public static void Update(Project project)
    {
        var path = project.ResourcePoolPath ?? throw new InvalidOperationException("This project does not use a resource pool.");
        var pool = File.Exists(path) ? ProjectLoader.Load(path) : new Project(Path.GetFileNameWithoutExtension(path), project.StartDate);
        Merge(from: project, into: pool);
        ProjectScheduler.Schedule(pool);
        if (Path.GetExtension(path).Equals(MspdiFile.Extension, StringComparison.OrdinalIgnoreCase))
            MspdiFile.Save(pool, path);
        else
            ProjectFile.Save(pool, path);
    }

    /// <summary>Copies resource details by name; returns the number of resources added.</summary>
    private static int Merge(Project from, Project into)
    {
        int added = 0;
        foreach (var source in from.Resources)
        {
            var target = into.Resources.FirstOrDefault(r => r.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                target = into.AddResource(source.Name, source.Type);
                added++;
            }
            else if (target.Type != source.Type && !into.AssignmentsOf(target).Any())
            {
                target.Type = source.Type;
            }

            target.Initials = source.Initials;
            target.Group = source.Group;
            target.Code = source.Code;
            target.Email = source.Email;
            target.MaterialLabel = source.MaterialLabel;
            target.MaxUnits = source.MaxUnits;
            target.AccrueAt = source.AccrueAt;
            target.IsBudget = source.IsBudget;
            target.IsGeneric = source.IsGeneric;
            target.BookingType = source.BookingType;
            target.CanLevel = source.CanLevel;
            target.Availability.Clear();
            target.Availability.AddRange(source.Availability);
            for (int i = 0; i < Resource.RateTableCount; i++)
            {
                target.CostRateTables[i].Clear();
                target.CostRateTables[i].AddRange(source.CostRateTables[i]);
            }
            if (target.Type == ResourceType.Work && source.Calendar is { } calendar)
            {
                // Resource calendars follow the pool, based on this project's calendar of the same name.
                var baseCalendar = into.Calendars.FirstOrDefault(c => c.Name == calendar.BaseCalendar?.Name) ?? into.Calendar;
                target.Calendar = calendar.Rebase(baseCalendar, target.Name);
            }
        }
        return added;
    }
}

/// <summary>A pool resource over-allocated when all sharing projects are counted: the days, the peak load and the projects involved.</summary>
public sealed record PoolOverallocation(string Resource, DateTime From, DateTime To, double PeakPercent, IReadOnlyList<string> Projects);
