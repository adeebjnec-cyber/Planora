using System.Windows;
using System.Windows.Input;
using Planora.App.Dialogs;
using Planora.App.Infrastructure;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.App.ViewModels;

/// <summary>Recurring tasks, splits, progress lines, baseline choice, highlight filters, options and budgets.</summary>
public sealed partial class MainViewModel
{
    private bool _showProgressLines;
    private int _ganttBaselineIndex;
    private bool _isSplitMode;
    private bool _highlightFilter;

    public static IReadOnlyList<string> BaselineNames { get; } =
        Enumerable.Range(0, Baseline.Count).Select(i => i == 0 ? "Baseline" : $"Baseline {i}").ToList();

    /// <summary>Draw progress lines through each task's progress at the status date.</summary>
    public bool ShowProgressLines
    {
        get => _showProgressLines;
        set
        {
            if (SetProperty(ref _showProgressLines, value))
                Refreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Which baseline the Gantt chart draws under the task bars.</summary>
    public int GanttBaselineIndex
    {
        get => _ganttBaselineIndex;
        set
        {
            if (SetProperty(ref _ganttBaselineIndex, Math.Clamp(value, 0, Baseline.Count - 1)))
                Refreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The next click on a task bar splits the task there (Task > Split Task).</summary>
    public bool IsSplitMode
    {
        get => _isSplitMode;
        set
        {
            if (SetProperty(ref _isSplitMode, value))
                Status = value ? "Click a task bar where the task should be split. Press Esc to cancel." : "Ready";
        }
    }

    /// <summary>Highlight tasks matching the filter instead of hiding the others.</summary>
    public bool HighlightFilter
    {
        get => _highlightFilter;
        set
        {
            if (SetProperty(ref _highlightFilter, value))
                ReorganizeRows();
        }
    }

    internal bool IsHighlighted(ProjectTask task)
    {
        if (!_highlightFilter)
            return false;
        var filter = ViewCatalog.Filter(Project, CurrentFilterName);
        return !filter.IsAllTasks && filter.Matches(Project, task);
    }

    public ICommand RecurringTaskCommand { get; private set; } = null!;
    public ICommand SplitTaskCommand { get; private set; } = null!;
    public ICommand SplitModeCommand { get; private set; } = null!;
    public ICommand OptionsCommand { get; private set; } = null!;
    public ICommand BudgetsCommand { get; private set; } = null!;

    private void InitializeExtraCommands()
    {
        RecurringTaskCommand = new RelayCommand(() => ShowDialog(new RecurringTaskDialog(this)));
        SplitTaskCommand = new RelayCommand(
            () => ShowDialog(new SplitTaskDialog(this, SelectedTasks[0])),
            () => SelectedTasks.Count == 1 && CanSplit(SelectedTasks[0]));
        SplitModeCommand = new RelayCommand(() => IsSplitMode = !IsSplitMode);
        OptionsCommand = new RelayCommand(() => ShowDialog(new OptionsDialog(this)));
        BudgetsCommand = new RelayCommand(() => ShowDialog(new BudgetDialog(this)));
    }

    public static bool CanSplit(ProjectTask task) =>
        !task.IsSummary && task.DurationMinutes > 0 && task.Mode == TaskMode.AutoScheduled;

    /// <summary>Opens the right information dialog for a task (recurring tasks have their own).</summary>
    private void ShowTaskInformation()
    {
        var task = SelectedTasks[0];
        if (task.IsRecurring)
            ShowDialog(new RecurringTaskDialog(this, task));
        else
            ShowDialog(new TaskInformationDialog(this, task));
    }

    /// <summary>Splits a task after the given amount of its work, pausing for <paramref name="gapMinutes"/>.</summary>
    public void SplitTask(ProjectTask task, long afterWorkMinutes, long gapMinutes)
    {
        int id = task.Id;
        IsSplitMode = false;
        TryEdit("Split task", p => p.GetRequiredTask(id).Split(afterWorkMinutes, gapMinutes));
    }

    public void ShowAssignmentInformation(Assignment assignment)
    {
        var dialog = new AssignmentInformationDialog(this, assignment) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }
}
