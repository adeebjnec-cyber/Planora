using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Interop;

/// <summary>Links to tasks in other project files: adding them and reading the other tasks' latest dates.</summary>
public static class ExternalLinks
{
    /// <summary>Adds a link from a task in another file, reading its dates now.</summary>
    public static ExternalLink Add(ProjectTask task, string projectPath, int taskId, LinkType type = LinkType.FinishToStart, long lagMinutes = 0)
    {
        var other = ProjectLoader.Load(projectPath);
        ProjectScheduler.Schedule(other);
        var source = other.GetTask(taskId) ?? throw new InvalidOperationException($"The task {taskId} is not in {Path.GetFileName(projectPath)}.");
        task.ExternalPredecessors.RemoveAll(l => SamePath(l.ProjectPath, projectPath) && l.TaskId == taskId);
        var link = new ExternalLink
        {
            ProjectPath = Path.GetFullPath(projectPath),
            TaskId = taskId,
            TaskName = source.Name,
            Type = type,
            LagMinutes = lagMinutes,
            Start = source.Start,
            Finish = source.Finish,
            RefreshedOn = DateTime.Now,
        };
        task.ExternalPredecessors.Add(link);
        return link;
    }

    /// <summary>
    /// Re-reads every linked file and updates the linked tasks' names and dates. Returns the links that
    /// could not be refreshed (file or task missing), which keep their last known dates.
    /// </summary>
    public static IReadOnlyList<(ProjectTask Task, ExternalLink Link, string Problem)> Refresh(Project project)
    {
        var problems = new List<(ProjectTask, ExternalLink, string)>();
        var cache = new Dictionary<string, Project?>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in project.Tasks)
        {
            foreach (var link in task.ExternalPredecessors)
            {
                if (!cache.TryGetValue(link.ProjectPath, out var other))
                {
                    try
                    {
                        other = File.Exists(link.ProjectPath) ? ProjectLoader.Load(link.ProjectPath) : null;
                        if (other is not null)
                            ProjectScheduler.Schedule(other);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        other = null;
                    }
                    cache[link.ProjectPath] = other;
                }
                if (other is null)
                {
                    problems.Add((task, link, $"{Path.GetFileName(link.ProjectPath)} could not be opened"));
                    continue;
                }
                if (other.GetTask(link.TaskId) is not { } source)
                {
                    problems.Add((task, link, $"task '{link.TaskName}' is no longer in {Path.GetFileName(link.ProjectPath)}"));
                    continue;
                }
                link.TaskName = source.Name;
                link.Start = source.Start;
                link.Finish = source.Finish;
                link.RefreshedOn = DateTime.Now;
            }
        }
        return problems;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
