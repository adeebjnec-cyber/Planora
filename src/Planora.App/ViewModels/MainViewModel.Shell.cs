using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Planora.App.Infrastructure;
using Planora.Engine.Fields;
using Planora.Engine.Model;

namespace Planora.App.ViewModels;

/// <summary>A schedule warning in the notifications list: a colour ("amber", "red" or "gray"), a title and a detail line.</summary>
public sealed record ScheduleWarning(string Level, string Title, string Detail);

/// <summary>
/// The application shell around the views: the breadcrumb, sidebar project card, status bar figures,
/// save state, schedule warnings, and expanding or collapsing summary tasks.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly HashSet<int> _collapsed = new();
    private DateTime? _lastSaved;
    private DispatcherTimer? _clock;

    public ICommand CalculateCommand { get; private set; } = null!;
    public ICommand ExpandAllCommand { get; private set; } = null!;
    public ICommand CollapseAllCommand { get; private set; } = null!;
    public ICommand ToggleCollapseCommand { get; private set; } = null!;
    public ICommand ToggleCriticalPathCommand { get; private set; } = null!;
    public ICommand SetTableCommand { get; private set; } = null!;
    public ICommand SetFilterCommand { get; private set; } = null!;
    public ICommand SetGroupCommand { get; private set; } = null!;

    /// <summary>The zoom level's name in the command bar.</summary>
    public string ZoomLabel => ZoomIndex switch
    {
        0 => "Years",
        1 => "Quarters",
        2 => "Months",
        3 => "Weeks",
        4 => "Days",
        5 => "Days +",
        6 => "Hours",
        _ => "Hours +",
    };

    private void InitializeShellCommands()
    {
        CalculateCommand = new RelayCommand(() =>
        {
            Document.Recalculate();
            Status = Project.Tasks.Count == 0 ? "Schedule recalculated" : $"Schedule recalculated · finish {TaskRowViewModel.FormatDate(Project.Finish)}";
        });
        ExpandAllCommand = new RelayCommand(() => SetCollapsed(Array.Empty<int>()), () => _collapsed.Count > 0);
        CollapseAllCommand = new RelayCommand(() => SetCollapsed(Project.Tasks.Where(t => t.IsSummary && t.OutlineLevel == 1).Select(t => t.Id)));
        ToggleCollapseCommand = new RelayCommand(row =>
        {
            if (row is TaskRowViewModel { Task: { IsSummary: true } task })
                ToggleCollapsed(task);
        });
        ToggleCriticalPathCommand = new RelayCommand(() => ShowCriticalPath = !ShowCriticalPath);
        SetTableCommand = new RelayCommand(name => { if (name is string n) CurrentTableName = n; });
        SetFilterCommand = new RelayCommand(name => { if (name is string n) CurrentFilterName = n; });
        SetGroupCommand = new RelayCommand(name => { if (name is string n) CurrentGroupName = n; });

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => OnPropertyChanged(nameof(LastSavedText));
        _clock.Start();
    }

    // ------------------------------------------------------------------ outline

    /// <summary>True if a summary task's subtasks are hidden.</summary>
    public bool IsCollapsed(ProjectTask task) => _collapsed.Contains(task.Id);

    public void ToggleCollapsed(ProjectTask task)
    {
        if (!_collapsed.Remove(task.Id))
            _collapsed.Add(task.Id);
        RefreshOutline();
    }

    private void SetCollapsed(IEnumerable<int> ids)
    {
        _collapsed.Clear();
        _collapsed.UnionWith(ids);
        RefreshOutline();
    }

    private void RefreshOutline()
    {
        SyncRows();
        Refreshed?.Invoke(this, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>A task is hidden when any summary above it is collapsed (only while the sheet shows the outline).</summary>
    private bool HiddenByOutline(ProjectTask task)
    {
        if (_collapsed.Count == 0 || !ShowsOutline)
            return false;
        for (var parent = task.Parent; parent is not null; parent = parent.Parent)
        {
            if (_collapsed.Contains(parent.Id))
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ breadcrumb and project card

    /// <summary>The breadcrumb's first part: the folder the project is saved in.</summary>
    public string ProjectParent =>
        Document.FilePath is { } path && Path.GetFileName(Path.GetDirectoryName(path)) is { Length: > 0 } folder ? folder : "Projects";

    /// <summary>The breadcrumb's title: the project's name, or its file name.</summary>
    public string ProjectTitle => Project.Name is { Length: > 0 } name ? name : Document.Title.TrimEnd(' ', '*');

    public bool HasBaseline => Project.Tasks.Any(t => t.Baselines[0] is not null);

    /// <summary>Not started, Active or Complete, from the project's progress.</summary>
    public string ProjectStatusText
    {
        get
        {
            if (Project.Tasks.Count == 0)
                return "Empty";
            double percent = Project.Tasks.Where(t => t.OutlineLevel == 1).Select(t => t.PercentComplete).DefaultIfEmpty(0).Average();
            return percent >= 100 ? "Complete" : Project.Tasks.Any(t => t.PercentComplete > 0) ? "Active" : "Not started";
        }
    }

    public string ProjectStatusLevel => ProjectStatusText switch { "Active" => "green", "Complete" => "green", _ => "gray" };

    // ------------------------------------------------------------------ status bar

    public int TaskCount => Project.Tasks.Count;
    public int CriticalCount => Project.Tasks.Count(t => !t.IsSummary && t.IsCritical);
    public int ResourceCount => Project.Resources.Count;

    public string ProjectDurationText => Project.Tasks.Count == 0 ? "—"
        : DurationText.Format(Math.Max(0, Project.Calendar.WorkingMinutesBetween(Project.Start, Project.Finish)), Project.Options, DurationUnit.Days);

    public string FinishText => Project.Tasks.Count == 0 ? "—" : Project.Finish.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public bool IsDirty => Document.IsDirty;

    public string SaveStateText => Document.IsDirty ? "Unsaved changes" : Document.FilePath is null ? "Not saved yet" : "Saved";

    public string LastSavedText
    {
        get
        {
            if (_lastSaved is not { } saved)
                return Document.FilePath is null ? "never" : File.Exists(Document.FilePath) ? Ago(File.GetLastWriteTime(Document.FilePath)) : "—";
            return Ago(saved);
        }
    }

    private static string Ago(DateTime moment)
    {
        var age = DateTime.Now - moment;
        if (age.TotalMinutes < 1)
            return "just now";
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes} min ago";
        return moment.Date == DateTime.Today ? moment.ToString("t", CultureInfo.CurrentCulture) : moment.ToString("d MMM, t", CultureInfo.CurrentCulture);
    }

    internal void MarkSaved()
    {
        _lastSaved = DateTime.Now;
        NotifyShell();
    }

    // ------------------------------------------------------------------ warnings

    public IReadOnlyList<ScheduleWarning> Warnings
    {
        get
        {
            var list = new List<ScheduleWarning>();
            var project = Project;
            if (Document.ScheduleError is { } error)
                list.Add(new("red", "The schedule could not be calculated", error));
            var status = TaskFields.Find(project, "Status");
            var late = status is null ? new List<ProjectTask>()
                : project.Tasks.Where(t => !t.IsSummary && status.Format(project, t) == "Late").ToList();
            if (late.Count == 1)
                list.Add(new("amber", $"{late[0].Name} is behind schedule", $"{late[0].PercentComplete:0}% complete"));
            else if (late.Count > 1)
                list.Add(new("amber", $"{late.Count} tasks are behind schedule", string.Join(", ", late.Take(3).Select(t => t.Name)) + (late.Count > 3 ? "…" : "")));
            var missed = project.Tasks.Where(t => t.MissesDeadline).ToList();
            if (missed.Count > 0)
                list.Add(new("red", missed.Count == 1 ? $"{missed[0].Name} misses its deadline" : $"{missed.Count} tasks miss their deadlines", "Finish after the deadline date"));
            var over = project.Resources.Where(r => r.IsOverallocated).ToList();
            if (over.Count > 0)
                list.Add(new("red", over.Count == 1 ? $"{over[0].Name} is overallocated" : $"{over.Count} resources are overallocated", "Use Resource > Level All, or change the assignments"));
            return list;
        }
    }

    public int WarningCount => Warnings.Count;

    /// <summary>Re-reads everything the shell shows (after edits, saves and selection changes).</summary>
    private void NotifyShell()
    {
        foreach (var name in new[]
        {
            nameof(ProjectParent), nameof(ProjectTitle), nameof(HasBaseline), nameof(ProjectStatusText), nameof(ProjectStatusLevel),
            nameof(TaskCount), nameof(CriticalCount), nameof(ResourceCount), nameof(ProjectDurationText), nameof(FinishText),
            nameof(IsDirty), nameof(SaveStateText), nameof(LastSavedText), nameof(Warnings), nameof(WarningCount),
        })
            OnPropertyChanged(name);
    }
}
