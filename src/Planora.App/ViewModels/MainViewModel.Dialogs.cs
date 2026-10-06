using System.Windows;
using System.Windows.Input;
using Planora.App.Dialogs;
using Planora.App.Infrastructure;

namespace Planora.App.ViewModels;

/// <summary>Commands that open dialogs.</summary>
public sealed partial class MainViewModel
{
    public ICommand TaskInformationCommand { get; private set; } = null!;
    public ICommand ResourceInformationCommand { get; private set; } = null!;
    public ICommand ProjectInformationCommand { get; private set; } = null!;
    public ICommand ChangeWorkingTimeCommand { get; private set; } = null!;
    public ICommand SetBaselineCommand { get; private set; } = null!;
    public ICommand ClearBaselineCommand { get; private set; } = null!;
    public ICommand LevelResourcesCommand { get; private set; } = null!;

    private void InitializeDialogCommands()
    {
        TaskInformationCommand = new RelayCommand(
            ShowTaskInformation,
            () => SelectedTasks.Count > 0);
        ResourceInformationCommand = new RelayCommand(
            () => ShowDialog(new ResourceInformationDialog(this, SelectedResources[0])),
            () => SelectedResources.Count > 0);
        ProjectInformationCommand = new RelayCommand(() => ShowDialog(new ProjectInformationDialog(this)));
        ChangeWorkingTimeCommand = new RelayCommand(() => ShowDialog(new ChangeWorkingTimeDialog(this)));
        SetBaselineCommand = new RelayCommand(() => ShowDialog(new BaselineDialog(this, clear: false)));
        ClearBaselineCommand = new RelayCommand(() => ShowDialog(new BaselineDialog(this, clear: true)));
        LevelResourcesCommand = new RelayCommand(() => ShowDialog(new LevelingDialog(this)));
    }

    private static void ShowDialog(Window dialog)
    {
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();
    }
}
