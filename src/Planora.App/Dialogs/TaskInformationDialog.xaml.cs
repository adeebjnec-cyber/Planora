using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>
/// Task Information (double-click a task): all of a task's settings on tabs. Edits are collected here
/// and applied as one undoable change when OK is pressed; invalid input keeps the dialog open.
/// </summary>
public partial class TaskInformationDialog : Window
{
    public static readonly string[] ConstraintNames =
    {
        "As Soon As Possible", "As Late As Possible", "Must Start On", "Must Finish On",
        "Start No Earlier Than", "Start No Later Than", "Finish No Earlier Than", "Finish No Later Than",
    };

    private readonly MainViewModel _owner;
    private readonly int _taskId;
    private readonly string _originalStart;
    private readonly string _originalFinish;
    private readonly string _originalDuration;

    public TaskInformationDialog(MainViewModel owner, ProjectTask task)
    {
        _owner = owner;
        _taskId = task.Id;
        var project = owner.Project;
        var options = project.Options;

        IsLeaf = !task.IsSummary;
        TaskName = task.Name;
        Duration = _originalDuration = DurationText.Format(task.DurationMinutes, options, isElapsed: task.IsElapsedDuration);
        IsEstimated = task.IsEstimated;
        PercentComplete = task.PercentComplete.ToString("0.#", CultureInfo.CurrentCulture);
        Priority = task.Priority.ToString(CultureInfo.CurrentCulture);
        IsAuto = task.Mode == TaskMode.AutoScheduled;
        IsInactive = !task.IsActive;
        Start = _originalStart = DateText.Format(task.Start);
        Finish = _originalFinish = DateText.Format(task.Finish);
        IsMilestone = task.IsMilestone;
        Deadline = DateText.Format(task.Deadline);
        Wbs = task.WbsCode;
        ConstraintIndex = (int)task.ConstraintType;
        ConstraintDate = DateText.Format(task.ConstraintDate);
        TaskTypeIndex = (int)task.Type;
        IsEffortDriven = task.IsEffortDriven;
        CalendarChoices = new[] { "None" }.Concat(project.Calendars.Select(c => c.Name)).ToArray();
        CalendarIndex = task.Calendar is null ? 0 : project.Calendars.IndexOf(task.Calendar) + 1;
        IgnoreResourceCalendar = task.IgnoreResourceCalendar;
        Hyperlink = task.Hyperlink;
        HyperlinkAddress = task.HyperlinkAddress;
        HyperlinkSubAddress = task.HyperlinkSubAddress;
        IsMarked = task.IsMarked;
        HideBar = task.HideBar;
        FixedCost = task.FixedCost.ToString("0.##", CultureInfo.CurrentCulture);
        FixedCostAccrualIndex = (int)task.FixedCostAccrual;
        Notes = task.Notes;
        ResourceNames = project.Resources.Where(r => !r.IsBudget).Select(r => r.Name).ToArray();

        foreach (var link in task.Predecessors)
        {
            if (project.GetTask(link.PredecessorId) is not { } pred)
                continue;
            var lag = link.LagFormat == LagFormat.PercentOfPredecessor ? link.LagPercent != 0 : link.LagMinutes != 0;
            PredecessorEntries.Add(new PredecessorEntry(project)
            {
                Row = (project.IndexOf(pred) + 1).ToString(CultureInfo.CurrentCulture),
                Type = PredecessorText.Abbreviation(link.Type),
                Lag = lag ? DurationText.FormatLag(link, options) : "0d",
            });
        }
        foreach (var a in task.Assignments)
        {
            AssignmentEntries.Add(new AssignmentEntry
            {
                ResourceName = a.Resource.Name,
                Units = a.Resource.Type == ResourceType.Work ? $"{a.Units * 100:0.##}%" : a.Units.ToString("0.##", CultureInfo.CurrentCulture),
            });
        }

        InitializeComponent();
        DataContext = this;
        Title = $"Task Information - {task.Name}";
    }

    public bool IsLeaf { get; }
    public string TaskName { get; set; }
    public string Duration { get; set; }
    public bool IsEstimated { get; set; }
    public string PercentComplete { get; set; }
    public string Priority { get; set; }
    public bool IsAuto { get; set; }
    public bool IsManual { get => !IsAuto; set => IsAuto = !value; }
    public bool IsInactive { get; set; }
    public string Start { get; set; }
    public string Finish { get; set; }
    public bool IsMilestone { get; set; }
    public string Deadline { get; set; }
    public string Wbs { get; }
    public IReadOnlyList<string> ConstraintTypes => ConstraintNames;
    public int ConstraintIndex { get; set; }
    public string ConstraintDate { get; set; }
    public IReadOnlyList<string> TaskTypes { get; } = new[] { "Fixed Units", "Fixed Duration", "Fixed Work" };
    public int TaskTypeIndex { get; set; }
    public bool IsEffortDriven { get; set; }
    public IReadOnlyList<string> CalendarChoices { get; }
    public int CalendarIndex { get; set; }
    public bool IgnoreResourceCalendar { get; set; }
    public string Hyperlink { get; set; } = "";
    public string HyperlinkAddress { get; set; } = "";
    public string HyperlinkSubAddress { get; set; } = "";
    public bool IsMarked { get; set; }
    public bool HideBar { get; set; }
    public string FixedCost { get; set; }
    public IReadOnlyList<string> Accruals => ResourceRowViewModel.Accruals;
    public int FixedCostAccrualIndex { get; set; }
    public string Notes { get; set; }
    public IReadOnlyList<string> ResourceNames { get; }
    public ObservableCollection<PredecessorEntry> PredecessorEntries { get; } = new();
    public ObservableCollection<AssignmentEntry> AssignmentEntries { get; } = new();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        // Commit a cell still being edited on the visible tab.
        VisualTreeSearch.FindChild<System.Windows.Controls.DataGrid>(this)?.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        if (_owner.TryEdit("Task information", Apply))
            DialogResult = true;
    }

    private void Apply(Project p)
    {
        var t = p.GetRequiredTask(_taskId);
        var cal = t.Calendar ?? p.Calendar;
        if (string.IsNullOrWhiteSpace(TaskName))
            throw new FormatException("The task needs a name.");
        t.Name = TaskName.Trim();
        t.Notes = Notes;
        t.IsActive = !IsInactive;

        if (!int.TryParse(Priority, NumberStyles.Integer, CultureInfo.CurrentCulture, out var priority) || priority is < 0 or > 1000)
            throw new FormatException("Priority must be a whole number from 0 to 1000.");
        t.Priority = priority;

        if (!decimal.TryParse(FixedCost, NumberStyles.Currency, CultureInfo.CurrentCulture, out var fixedCost) || fixedCost < 0)
            throw new FormatException("Fixed cost must be a positive amount.");
        t.FixedCost = fixedCost;
        t.FixedCostAccrual = (CostAccrual)FixedCostAccrualIndex;
        t.Calendar = CalendarIndex <= 0 ? null : p.Calendars[CalendarIndex - 1];
        t.IgnoreResourceCalendar = IgnoreResourceCalendar;
        t.Hyperlink = Hyperlink.Trim();
        t.HyperlinkAddress = HyperlinkAddress.Trim();
        t.HyperlinkSubAddress = HyperlinkSubAddress.Trim();
        t.IsMarked = IsMarked;
        t.HideBar = HideBar;
        t.Deadline = ParseOptionalDate(Deadline, cal, endOfDay: true, "deadline");

        if (!t.IsSummary)
        {
            t.Mode = IsAuto ? TaskMode.AutoScheduled : TaskMode.ManuallyScheduled;
            t.IsMilestone = IsMilestone;
            t.Type = (TaskType)TaskTypeIndex;
            t.IsEffortDriven = IsEffortDriven;

            if (Duration != _originalDuration || IsEstimated != t.IsEstimated)
            {
                if (!DurationText.TryParse(Duration, p.Options, out var d))
                    throw new FormatException($"'{Duration}' is not a valid duration.");
                t.IsElapsedDuration = d.IsElapsed;
                t.IsEstimated = d.IsEstimated || IsEstimated;
                p.SetDuration(t, d.Minutes);
            }

            t.ConstraintType = (ConstraintType)ConstraintIndex;
            t.ConstraintDate = t.ConstraintType is ConstraintType.AsSoonAsPossible or ConstraintType.AsLateAsPossible
                ? null
                : ParseOptionalDate(ConstraintDate, cal, endOfDay: t.ConstraintType is ConstraintType.MustFinishOn or ConstraintType.FinishNoEarlierThan or ConstraintType.FinishNoLaterThan, "constraint date")
                  ?? throw new FormatException("This constraint type needs a constraint date.");

            if (t.Mode == TaskMode.ManuallyScheduled)
            {
                t.ManualStart = ParseOptionalDate(Start, cal, false, "start") ?? t.Start;
                t.ManualFinish = Finish != _originalFinish
                    ? ParseOptionalDate(Finish, cal, true, "finish")
                    : cal.AddWorkingMinutes(cal.NextWorkingMoment(t.ManualStart.Value), t.DurationMinutes);
            }
            else if (Start != _originalStart)
            {
                t.ConstraintType = ConstraintType.StartNoEarlierThan;
                t.ConstraintDate = ParseOptionalDate(Start, cal, false, "start");
            }
            else if (Finish != _originalFinish)
            {
                t.ConstraintType = ConstraintType.FinishNoEarlierThan;
                t.ConstraintDate = ParseOptionalDate(Finish, cal, true, "finish");
            }

            var resources = AssignmentEntries
                .Where(a => !string.IsNullOrWhiteSpace(a.ResourceName))
                .Select(a => string.IsNullOrWhiteSpace(a.Units) ? a.ResourceName!.Trim() : $"{a.ResourceName!.Trim()}[{a.Units.Trim()}]");
            ResourceNamesText.Apply(p, t, string.Join(", ", resources));
        }

        var predecessors = PredecessorEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.Row))
            .Select(e => e.Row!.Trim() + (e.Type ?? "FS") + FormatLag(e.Lag));
        PredecessorText.Apply(p, t, string.Join(",", predecessors));

        if (!double.TryParse(PercentComplete.TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) || percent is < 0 or > 100)
            throw new FormatException("Percent complete must be from 0 to 100.");
        if (Math.Abs(percent - t.PercentComplete) > 0.001 && !t.IsSummary)
            TaskRowViewModel.SetProgress(t, percent);
    }

    private static string FormatLag(string? lag)
    {
        var text = (lag ?? "").Trim();
        if (text.Length == 0 || text is "0" or "0d" or "0 days")
            return "";
        return text.StartsWith('-') || text.StartsWith('+') ? text : "+" + text;
    }

    private static DateTime? ParseOptionalDate(string? text, Engine.Calendars.WorkingCalendar calendar, bool endOfDay, string what)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("NA", StringComparison.OrdinalIgnoreCase))
            return null;
        return DateText.TryParse(text, calendar, endOfDay, out var date)
            ? date
            : throw new FormatException($"'{text}' is not a valid {what}.");
    }
}

/// <summary>A row of the Predecessors tab.</summary>
public sealed class PredecessorEntry : ObservableObject
{
    private readonly Project? _project;
    private string? _row;

    public PredecessorEntry()
    {
        _project = (Application.Current.MainWindow?.DataContext as MainViewModel)?.Project;
    }

    public PredecessorEntry(Project project)
    {
        _project = project;
    }

    public string? Row
    {
        get => _row;
        set
        {
            if (SetProperty(ref _row, value))
                OnPropertyChanged(nameof(TaskName));
        }
    }

    public string TaskName =>
        int.TryParse(Row, out var n) && _project is not null && n >= 1 && n <= _project.Tasks.Count ? _project.Tasks[n - 1].Name : "";

    public string? Type { get; set; } = "FS";
    public string? Lag { get; set; } = "0d";
}

/// <summary>A row of the Resources tab.</summary>
public sealed class AssignmentEntry
{
    public string? ResourceName { get; set; }
    public string Units { get; set; } = "100%";
}
