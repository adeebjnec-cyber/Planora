using System.Globalization;
using System.Windows.Input;
using Planora.App.Dialogs;
using Planora.App.Infrastructure;
using Planora.Engine.Scheduling;

namespace Planora.App.ViewModels;

/// <summary>Tracking: % complete buttons, Mark on Track, Update Tasks and Update Project.</summary>
public sealed partial class MainViewModel
{
    public ICommand PercentCompleteCommand { get; private set; } = null!;
    public ICommand MarkOnTrackCommand { get; private set; } = null!;
    public ICommand UpdateTasksCommand { get; private set; } = null!;
    public ICommand UpdateProjectCommand { get; private set; } = null!;

    private void InitializeTrackingCommands()
    {
        PercentCompleteCommand = new RelayCommand(p => SetSelectedProgress(Convert.ToDouble(p, CultureInfo.InvariantCulture)), _ => SelectedTasks.Count > 0);
        MarkOnTrackCommand = new RelayCommand(MarkSelectedOnTrack, () => SelectedTasks.Count > 0);
        UpdateTasksCommand = new RelayCommand(() => ShowDialog(new UpdateTasksDialog(this)), () => SelectedTasks.Count > 0);
        UpdateProjectCommand = new RelayCommand(() => ShowDialog(new UpdateProjectDialog(this)));
    }

    /// <summary>Leaf tasks of the selection (a selected summary stands for its subtasks).</summary>
    internal List<int> SelectedLeafIds() =>
        SelectedTasks.SelectMany(Project.Subtree).Where(t => !t.IsSummary).Select(t => t.Id).Distinct().ToList();

    private void SetSelectedProgress(double percent)
    {
        var ids = SelectedLeafIds();
        TryEdit($"{percent:0}% complete", p =>
        {
            foreach (var id in ids)
                TaskProgress.SetPercentComplete(p.GetRequiredTask(id), percent);
        });
    }

    /// <summary>Mark on Track: progress as scheduled up to the status date (or today when none is set).</summary>
    private void MarkSelectedOnTrack()
    {
        var ids = SelectedLeafIds();
        TryEdit("Mark on track", p =>
        {
            var statusDate = p.StatusDate ?? DateTime.Now;
            foreach (var id in ids)
                TaskProgress.MarkOnTrack(p, p.GetRequiredTask(id), statusDate);
        });
    }
}
