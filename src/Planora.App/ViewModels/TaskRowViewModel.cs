using System.Globalization;
using Planora.App.Infrastructure;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.App.ViewModels;

/// <summary>
/// One row of the task sheet. Getters format engine values; setters parse the typed text and apply it
/// through <see cref="MainViewModel.TryEdit"/> so every change is undoable and rescheduled.
/// The last row is a placeholder: typing a name there creates a new task.
/// </summary>
public sealed class TaskRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public TaskRowViewModel(MainViewModel owner, ProjectTask? task)
    {
        _owner = owner;
        Task = task;
    }

    /// <summary>A group header row (when the sheet is grouped).</summary>
    public TaskRowViewModel(MainViewModel owner, TaskGroup group)
    {
        _owner = owner;
        Group = group;
    }

    /// <summary>The task shown in this row; null for group headers and the blank "new task" row.</summary>
    public ProjectTask? Task { get; private set; }

    /// <summary>The group shown in this row, for group header rows.</summary>
    public TaskGroup? Group { get; }

    public bool IsPlaceholder => Task is null && Group is null;
    public bool IsGroupHeader => Group is not null;

    /// <summary>Indent level inside a grouped sheet: tasks sit one level below their innermost group.</summary>
    internal int GroupDepth { get; set; } = -1;

    private Project Project => _owner.Document.Project;
    private ScheduleOptions Options => Project.Options;

    internal void Rebind(ProjectTask? task)
    {
        Task = task;
        OnAllPropertiesChanged();
    }

    internal void Refresh() => OnAllPropertiesChanged();

    // ---- Display-only ----

    public string RowNumber => Task is null ? "" : (Project.IndexOf(Task) + 1).ToString(CultureInfo.CurrentCulture);
    public int OutlineLevel => Task?.OutlineLevel ?? 1;

    public double IndentWidth => Group is not null ? Group.Level * 16
        : GroupDepth >= 0 ? (GroupDepth + 1) * 16
        : _owner.ShowsOutline ? (OutlineLevel - 1) * 16 : 0;

    /// <summary>
    /// Any field's display text, by field key (used by table columns). Custom fields can be set through
    /// this indexer; dates use the sheet's short date format.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (Group is { } g)
            {
                return key switch
                {
                    "Name" => g.Label,
                    "Start" => DateText.Format(g.Start),
                    "Finish" => DateText.Format(g.Finish),
                    "Duration" => DurationText.Format(Math.Max(0, Project.Calendar.WorkingMinutesBetween(g.Start, g.Finish)), Options),
                    "Work" => DurationText.Format(g.WorkMinutes, Options, DurationUnit.Hours),
                    "Cost" => g.Cost.ToString("C", CultureInfo.CurrentCulture),
                    "PercentComplete" => $"{g.PercentComplete:0.#}%",
                    _ => "",
                };
            }
            if (Task is null || Engine.Fields.TaskFields.Find(Project, key) is not { } field)
                return "";
            return field.Get(Project, Task) is DateTime date ? DateText.Format(date) : field.Format(Project, Task);
        }
        set
        {
            if (Task is null || Engine.Fields.CustomField.TypeOfKey(key) is null || value == this[key])
                return;
            int id = Task.Id;
            _owner.TryEdit("Change " + Engine.Fields.TaskFields.Get(Project, key).Name, p =>
            {
                if (!Engine.Fields.TaskFields.TrySetCustom(p, p.GetRequiredTask(id), key, value))
                    throw new FormatException($"'{value}' is not a valid value for {Engine.Fields.TaskFields.Get(p, key).Name}.");
            });
        }
    }
    public bool IsSummary => Task?.IsSummary ?? false;
    public bool IsCritical => Task?.IsCritical ?? false;
    public bool IsHighlighted => Task is not null && _owner.IsHighlighted(Task);
    public bool IsManual => Task?.Mode == TaskMode.ManuallyScheduled;
    public bool IsInactive => Task is { IsActive: false };
    public bool HasNotes => !string.IsNullOrWhiteSpace(Task?.Notes);
    public bool MissesDeadline => Task?.MissesDeadline ?? false;
    public bool HasConstraint => Task is { ConstraintType: not (ConstraintType.AsSoonAsPossible or ConstraintType.AsLateAsPossible) };
    public bool IsRecurring => Task?.IsRecurring ?? false;
    public string TotalSlack => Task is null ? "" : DurationText.Format(Task.TotalFloatMinutes, Options);
    public string Work => Group is not null ? this["Work"] : Task is null ? "" : DurationText.Format(Task.WorkMinutes, Options, DurationUnit.Hours);
    public string Cost => Group is not null ? this["Cost"] : Task is null ? "" : Task.Cost.ToString("C", CultureInfo.CurrentCulture);

    /// <summary>"Indicators" column: short markers for notes, constraints, deadlines, recurrence.</summary>
    public string Indicators
    {
        get
        {
            if (Task is null)
                return "";
            var parts = new List<string>();
            if (Task.IsSubproject) parts.Add("⧉");
            if (IsRecurring) parts.Add("↻");
            if (HasConstraint) parts.Add("▦");
            if (MissesDeadline) parts.Add("!");
            if (HasNotes) parts.Add("✎");
            if (Task.PercentComplete >= 100) parts.Add("✓");
            return string.Join(" ", parts);
        }
    }

    // ---- Editable columns ----

    public string Name
    {
        get => Group?.Label ?? Task?.Name ?? "";
        set
        {
            if (Group is not null)
                return;
            if (Task is null)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    _owner.AddTaskFromPlaceholder(value.Trim());
                return;
            }
            if (value != Task.Name)
                Edit("Rename task", (_, t) => t.Name = value);
        }
    }

    public bool IsManuallyScheduled
    {
        get => IsManual;
        set
        {
            if (Task is null || value == IsManual || Task.IsSummary)
                return;
            Edit(value ? "Manually schedule task" : "Auto schedule task", (_, t) =>
            {
                if (value)
                {
                    t.ManualStart = t.Start;
                    t.ManualFinish = t.Finish;
                }
                t.Mode = value ? TaskMode.ManuallyScheduled : TaskMode.AutoScheduled;
            });
        }
    }

    public string Duration
    {
        get => Group is not null ? this["Duration"] : Task is null ? "" : DurationText.Format(Task.DurationMinutes, Options, isElapsed: Task.IsElapsedDuration, isEstimated: Task.IsEstimated);
        set
        {
            if (Task is null || value == Duration)
                return;
            if (Task.IsSummary)
            {
                _owner.ShowError("A summary task's duration comes from its subtasks.");
                OnPropertyChanged();
                return;
            }
            if (!DurationText.TryParse(value, Options, out var d))
            {
                _owner.ShowError($"'{value}' is not a valid duration. Examples: 3d, 2w, 4h, 5ed, 1mo?");
                OnPropertyChanged();
                return;
            }
            Edit("Change duration", (p, t) =>
            {
                t.IsElapsedDuration = d.IsElapsed;
                t.IsEstimated = d.IsEstimated;
                p.SetDuration(t, d.Minutes);
                if (t.Mode == TaskMode.ManuallyScheduled && t.ManualStart is { } start)
                    t.ManualFinish = (t.Calendar ?? p.Calendar).AddWorkingMinutes(start, d.Minutes);
            });
        }
    }

    public string Start
    {
        get => Group is not null ? this["Start"] : Task is null ? "" : FormatDate(Task.Start);
        set
        {
            if (Task is null || value == Start || Task.IsSummary)
            {
                OnPropertyChanged();
                return;
            }
            if (!TryParseDate(value, endOfDay: false, out var date))
            {
                _owner.ShowError($"'{value}' is not a valid date.");
                OnPropertyChanged();
                return;
            }
            Edit("Change start", (p, t) =>
            {
                if (t.Mode == TaskMode.ManuallyScheduled)
                {
                    var cal = t.Calendar ?? p.Calendar;
                    t.ManualStart = date;
                    t.ManualFinish = cal.AddWorkingMinutes(cal.NextWorkingMoment(date), t.DurationMinutes);
                }
                else
                {
                    // As in Microsoft Project, typing a start date on an auto-scheduled task sets a
                    // Start No Earlier Than constraint (Start No Later Than when scheduling from finish).
                    t.ConstraintType = p.ScheduleFromFinish ? ConstraintType.StartNoLaterThan : ConstraintType.StartNoEarlierThan;
                    t.ConstraintDate = date;
                }
            });
        }
    }

    public string Finish
    {
        get => Group is not null ? this["Finish"] : Task is null ? "" : FormatDate(Task.Finish);
        set
        {
            if (Task is null || value == Finish || Task.IsSummary)
            {
                OnPropertyChanged();
                return;
            }
            if (!TryParseDate(value, endOfDay: true, out var date))
            {
                _owner.ShowError($"'{value}' is not a valid date.");
                OnPropertyChanged();
                return;
            }
            Edit("Change finish", (p, t) =>
            {
                var cal = t.Calendar ?? p.Calendar;
                if (t.Mode == TaskMode.ManuallyScheduled)
                {
                    t.ManualFinish = date;
                    p.SetDuration(t, Math.Max(0, cal.WorkingMinutesBetween(t.ManualStart ?? t.Start, date)));
                }
                else
                {
                    t.ConstraintType = p.ScheduleFromFinish ? ConstraintType.FinishNoLaterThan : ConstraintType.FinishNoEarlierThan;
                    t.ConstraintDate = date;
                }
            });
        }
    }

    public string Predecessors
    {
        get => Task is null ? "" : PredecessorText.Format(Project, Task);
        set
        {
            if (Task is null || value == Predecessors)
                return;
            Edit("Change predecessors", (p, t) => PredecessorText.Apply(p, t, value));
        }
    }

    public string ResourceNames
    {
        get => Task is null ? "" : ResourceNamesText.Format(Task);
        set
        {
            if (Task is null || value == ResourceNames)
                return;
            if (Task.IsSummary)
            {
                _owner.ShowError("Assign resources to subtasks rather than summary tasks.");
                OnPropertyChanged();
                return;
            }
            Edit("Assign resources", (p, t) => ResourceNamesText.Apply(p, t, value));
        }
    }

    public string PercentComplete
    {
        get => Group is not null ? this["PercentComplete"] : Task is null ? "" : $"{Task.PercentComplete:0.#}%";
        set
        {
            if (Task is null || value == PercentComplete)
                return;
            if (!double.TryParse(value.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) || percent is < 0 or > 100)
            {
                _owner.ShowError("Percent complete must be a number from 0 to 100.");
                OnPropertyChanged();
                return;
            }
            if (Task.IsSummary)
                Edit("Update progress", (p, t) => SetSummaryProgress(t, percent));
            else
                Edit("Update progress", (_, t) => SetProgress(t, percent));
        }
    }

    /// <summary>Sets % complete and, like Microsoft Project, fills in actual start/finish.</summary>
    internal static void SetProgress(ProjectTask task, double percent)
    {
        task.PercentComplete = percent;
        if (percent > 0)
            task.ActualStart ??= task.Start;
        else
            task.ActualStart = null;
        task.ActualFinish = percent >= 100 ? task.Finish : null;
    }

    private static void SetSummaryProgress(ProjectTask summary, double percent)
    {
        foreach (var child in summary.Children)
        {
            if (child.IsSummary)
                SetSummaryProgress(child, percent);
            else
                SetProgress(child, percent);
        }
    }

    // ---- Helpers ----

    private void Edit(string description, Action<Project, ProjectTask> change)
    {
        int id = Task!.Id;
        _owner.TryEdit(description, p => change(p, p.GetRequiredTask(id)));
    }

    public static string FormatDate(DateTime date) => DateText.Format(date);

    private bool TryParseDate(string text, bool endOfDay, out DateTime date) =>
        DateText.TryParse(text, Task?.Calendar ?? Project.Calendar, endOfDay, out date);
}