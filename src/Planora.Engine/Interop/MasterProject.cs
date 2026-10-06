using Planora.Engine.Calendars;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Interop;

/// <summary>Opens Planora (.planora) or Microsoft Project XML (.xml) files by extension.</summary>
public static class ProjectLoader
{
    public static Project Load(string path) =>
        Path.GetExtension(path).Equals(MspdiFile.Extension, StringComparison.OrdinalIgnoreCase) ? MspdiFile.Load(path) : ProjectFile.Load(path);
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
    /// <summary>Uses the pool's resources in this project (matching by name, adding the rest). Returns how many were added.</summary>
    public static int Share(Project project, string poolPath)
    {
        var pool = ProjectLoader.Load(poolPath);
        project.ResourcePoolPath = poolPath;
        return Merge(from: pool, into: project);
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
