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
        var filter = ActiveFilter();
        return !filter.IsAllTasks && filter.Matches(Project, task);
    }

    public ICommand RecurringTaskCommand { get; private set; } = null!;
    public ICommand SplitTaskCommand { get; private set; } = null!;
    public ICommand SplitModeCommand { get; private set; } = null!;
    public ICommand OptionsCommand { get; private set; } = null!;
    public ICommand BudgetsCommand { get; private set; } = null!;
    public ICommand InsertProjectCommand { get; private set; } = null!;
    public ICommand RefreshSubprojectCommand { get; private set; } = null!;
    public ICommand SaveSubprojectCommand { get; private set; } = null!;
    public ICommand ShareResourcesCommand { get; private set; } = null!;
    public ICommand RefreshPoolCommand { get; private set; } = null!;
    public ICommand UpdatePoolCommand { get; private set; } = null!;

    private void InitializeExtraCommands()
    {
        RecurringTaskCommand = new RelayCommand(() => ShowDialog(new RecurringTaskDialog(this)));
        SplitTaskCommand = new RelayCommand(
            () => ShowDialog(new SplitTaskDialog(this, SelectedTasks[0])),
            () => SelectedTasks.Count == 1 && CanSplit(SelectedTasks[0]));
        SplitModeCommand = new RelayCommand(() => IsSplitMode = !IsSplitMode);
        OptionsCommand = new RelayCommand(() => ShowDialog(new OptionsDialog(this)));
        BudgetsCommand = new RelayCommand(() => ShowDialog(new BudgetDialog(this)));
        InsertProjectCommand = new RelayCommand(InsertProject);
        RefreshSubprojectCommand = new RelayCommand(() => SubprojectAction("Refresh subproject", Engine.Interop.MasterProject.RefreshSubproject), () => SelectedSubproject is not null);
        SaveSubprojectCommand = new RelayCommand(SaveSubproject, () => SelectedSubproject is not null);
        ShareResourcesCommand = new RelayCommand(ShareResources);
        RefreshPoolCommand = new RelayCommand(() => PoolAction("Refresh from resource pool", p => Engine.Interop.ResourcePool.Refresh(p)), () => Project.ResourcePoolPath is not null);
        UpdatePoolCommand = new RelayCommand(UpdatePool, () => Project.ResourcePoolPath is not null);
    }

    private const string ProjectFilesFilter = "Projects (*.planora;*.xml)|*.planora;*.xml|Planora project (*.planora)|*.planora|Microsoft Project XML (*.xml)|*.xml";

    /// <summary>The inserted-project summary task containing the selection, if any.</summary>
    private ProjectTask? SelectedSubproject
    {
        get
        {
            for (var t = SelectedTasks.FirstOrDefault(); t is not null; t = t.Parent)
            {
                if (t.IsSubproject)
                    return t;
            }
            return null;
        }
    }

    private void InsertProject()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = ProjectFilesFilter, Title = "Insert Project" };
        if (dialog.ShowDialog() != true)
            return;
        int index = SelectedTasks.Count > 0 ? Project.IndexOf(SelectedTasks[0]) : Project.Tasks.Count;
        TryEdit("Insert project", p => Engine.Interop.MasterProject.InsertSubproject(p, dialog.FileName, index));
    }

    private void SubprojectAction(string description, Action<Project, ProjectTask> action)
    {
        int id = SelectedSubproject!.Id;
        TryEdit(description, p => action(p, p.GetRequiredTask(id)));
    }

    private void SaveSubproject()
    {
        var summary = SelectedSubproject!;
        if (MessageBox.Show(Application.Current.MainWindow, $"Save the tasks under '{summary.Name}' back to {summary.SubprojectPath}?",
                "Save Subproject", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        try
        {
            Engine.Interop.MasterProject.SaveSubproject(Project, summary);
            Status = $"Saved subproject {System.IO.Path.GetFileName(summary.SubprojectPath)}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save the subproject: {ex.Message}");
        }
    }

    private void ShareResources()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = ProjectFilesFilter, Title = "Share Resources: choose the resource pool file" };
        if (dialog.ShowDialog() == true)
            PoolAction("Share resources", p => Engine.Interop.ResourcePool.Share(p, dialog.FileName, Document.FilePath));
    }

    private void PoolAction(string description, Func<Project, int> action)
    {
        int added = 0;
        if (TryEdit(description, p => added = action(p)))
            Status = added == 0 ? "Resources updated from the pool" : $"Resources updated from the pool; {added} added";
    }

    public ICommand LinkExternalCommand { get; private set; } = null!;
    public ICommand RefreshExternalLinksCommand { get; private set; } = null!;
    public ICommand PoolOverallocationsCommand { get; private set; } = null!;

    private void InitializeMultiProjectCommands()
    {
        LinkExternalCommand = new RelayCommand(() => ShowDialog(new ExternalLinkDialog(this, SelectedTasks[0])), () => SelectedTasks.Count == 1 && !SelectedTasks[0].IsSummary);
        RefreshExternalLinksCommand = new RelayCommand(RefreshExternalLinks, () => Project.Tasks.Any(t => t.ExternalPredecessors.Count > 0));
        PoolOverallocationsCommand = new RelayCommand(() => ShowDialog(new PoolOverallocationsDialog(this)), () => Project.ResourcePoolPath is not null);
    }

    /// <summary>Re-reads the dates of predecessor tasks in other project files.</summary>
    private void RefreshExternalLinks()
    {
        IReadOnlyList<(ProjectTask Task, ExternalLink Link, string Problem)> problems = Array.Empty<(ProjectTask, ExternalLink, string)>();
        if (!TryEdit("Refresh external links", p => problems = Engine.Interop.ExternalLinks.Refresh(p)))
            return;
        if (problems.Count == 0)
            Status = "External links refreshed";
        else
            ShowError("Some external links kept their last known dates:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(x => $"{x.Task.Name}: {x.Problem}")));
    }

    private void UpdatePool()
    {
        try
        {
            Engine.Interop.ResourcePool.Update(Project);
            Status = $"Resource pool {System.IO.Path.GetFileName(Project.ResourcePoolPath)} updated";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            ShowError($"Could not update the resource pool: {ex.Message}");
        }
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

    /// <summary>Sets an assignment's work in one period (a cell of the usage views).</summary>
    public void SetTimephasedWork(Assignment assignment, DateTime from, DateTime to, long workMinutes)
    {
        var (taskId, resourceId) = (assignment.Task.Id, assignment.Resource.Id);
        TryEdit("Edit work", p =>
        {
            var a = p.GetRequiredTask(taskId).Assignments.Single(x => x.Resource.Id == resourceId);
            p.SetTimephasedWork(a, from, to, workMinutes);
        });
    }
    public void ShowAssignmentInformation(Assignment assignment)
    {
        var dialog = new AssignmentInformationDialog(this, assignment) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }
}
