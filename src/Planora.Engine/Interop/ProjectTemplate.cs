using Planora.Engine.Model;
using Planora.Engine.Persistence;

namespace Planora.Engine.Interop;

/// <summary>
/// Project templates: a project file (same format, ".planoratemplate") with progress and baselines
/// removed. Creating a project from a template moves its dates to a new start.
/// </summary>
public static class ProjectTemplate
{
    public const string Extension = ".planoratemplate";

    /// <summary>Saves a copy of the project as a template, clearing the chosen kinds of data (as Microsoft Project's Save As Template asks).</summary>
    public static void Save(Project project, string path, bool removeProgress = true, bool removeBaselines = true, bool removeRates = false)
    {
        var copy = ProjectFile.Deserialize(ProjectFile.Serialize(project));
        if (removeProgress)
        {
            copy.StatusDate = null;
            foreach (var task in copy.Tasks)
            {
                task.PercentComplete = 0;
                task.ActualStart = null;
                task.ActualFinish = null;
            }
        }
        if (removeBaselines)
        {
            for (int i = 0; i < Baseline.Count; i++)
                copy.ClearBaseline(i);
            foreach (var task in copy.Tasks)
                Array.Clear(task.InterimPlans);
        }
        if (removeRates)
        {
            foreach (var resource in copy.Resources)
            {
                foreach (var table in resource.CostRateTables)
                {
                    table.Clear();
                    table.Add(new CostRate(DateTime.MinValue, 0));
                }
            }
        }
        ProjectFile.Save(copy, path);
    }

    /// <summary>Loads a template as a new, unsaved project starting at <paramref name="start"/>.</summary>
    public static Project Instantiate(string path, DateTime start, string? name = null)
    {
        var project = ProjectFile.Load(path);
        MoveStart(project, start);
        project.Name = name ?? Path.GetFileNameWithoutExtension(path);
        return project;
    }

    /// <summary>Moves the project start, shifting every fixed date (constraints, deadlines, manual dates) by the same number of days.</summary>
    public static void MoveStart(Project project, DateTime start)
    {
        var shift = start.Date - project.StartDate.Date;
        DateTime? Move(DateTime? date) => date + shift;

        project.StartDate = start;
        project.FinishDate += shift;
        project.StatusDate = null;
        foreach (var task in project.Tasks)
        {
            task.ConstraintDate = Move(task.ConstraintDate);
            task.Deadline = Move(task.Deadline);
            task.ManualStart = Move(task.ManualStart);
            task.ManualFinish = Move(task.ManualFinish);
            task.ActualStart = Move(task.ActualStart);
            task.ActualFinish = Move(task.ActualFinish);
            if (task.Recurrence is { } recurrence)
            {
                recurrence.Start = recurrence.Start.AddDays(shift.Days);
                if (recurrence.End is { } end)
                    recurrence.End = end.AddDays(shift.Days);
            }
        }
    }
}
