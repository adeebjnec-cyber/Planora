using System.Windows.Input;
using Planora.App.Dialogs;
using Planora.App.Infrastructure;

namespace Planora.App.ViewModels;

/// <summary>Format tab: bar styles, text styles, timescale and gridlines.</summary>
public sealed partial class MainViewModel
{
    public ICommand BarStylesCommand { get; private set; } = null!;
    public ICommand TextStylesCommand { get; private set; } = null!;
    public ICommand TimescaleCommand { get; private set; } = null!;
    public ICommand GridlinesCommand { get; private set; } = null!;
    public ICommand MacrosCommand { get; private set; } = null!;
    public ICommand PageSetupCommand { get; private set; } = null!;

    private Engine.Scheduling.TaskPathKind _taskPath;

    /// <summary>Format > Task Path: which links of the selected task to highlight on the Gantt Chart.</summary>
    public Engine.Scheduling.TaskPathKind TaskPath
    {
        get => _taskPath;
        set
        {
            if (!SetProperty(ref _taskPath, value))
                return;
            OnPropertyChanged(nameof(ShowPathPredecessors));
            OnPropertyChanged(nameof(ShowPathDrivingPredecessors));
            OnPropertyChanged(nameof(ShowPathSuccessors));
            OnPropertyChanged(nameof(ShowPathDrivenSuccessors));
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetPath(Engine.Scheduling.TaskPathKind kind, bool on) => TaskPath = on ? TaskPath | kind : TaskPath & ~kind;

    public bool ShowPathPredecessors { get => TaskPath.HasFlag(Engine.Scheduling.TaskPathKind.Predecessors); set => SetPath(Engine.Scheduling.TaskPathKind.Predecessors, value); }
    public bool ShowPathDrivingPredecessors { get => TaskPath.HasFlag(Engine.Scheduling.TaskPathKind.DrivingPredecessors); set => SetPath(Engine.Scheduling.TaskPathKind.DrivingPredecessors, value); }
    public bool ShowPathSuccessors { get => TaskPath.HasFlag(Engine.Scheduling.TaskPathKind.Successors); set => SetPath(Engine.Scheduling.TaskPathKind.Successors, value); }
    public bool ShowPathDrivenSuccessors { get => TaskPath.HasFlag(Engine.Scheduling.TaskPathKind.DrivenSuccessors); set => SetPath(Engine.Scheduling.TaskPathKind.DrivenSuccessors, value); }

    private void InitializeFormatCommands()
    {
        BarStylesCommand = new RelayCommand(() => ShowDialog(new BarStylesDialog(this)));
        TextStylesCommand = new RelayCommand(() => ShowDialog(new TextStylesDialog(this)));
        TimescaleCommand = new RelayCommand(() => ShowDialog(new TimescaleDialog(this)));
        GridlinesCommand = new RelayCommand(() => ShowDialog(new GridlinesDialog(this)));
        MacrosCommand = new RelayCommand(() => ShowDialog(new MacrosDialog(this)));
        PageSetupCommand = new RelayCommand(() => ShowDialog(new PageSetupDialog(this)));
    }
}
