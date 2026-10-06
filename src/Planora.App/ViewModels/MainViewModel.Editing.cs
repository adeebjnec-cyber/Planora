using System.Windows.Input;
using Planora.App.Dialogs;
using Planora.App.Infrastructure;
using Planora.Engine.Model;

namespace Planora.App.ViewModels;

/// <summary>Editing: Find, Replace, Fill Down and Spelling.</summary>
public sealed partial class MainViewModel
{
    public ICommand FindCommand { get; private set; } = null!;
    public ICommand ReplaceCommand { get; private set; } = null!;
    public ICommand FillDownCommand { get; private set; } = null!;
    public ICommand SpellingCommand { get; private set; } = null!;

    /// <summary>Asks the task view to select one task and scroll to it.</summary>
    public event EventHandler<ProjectTask>? SelectTaskRequested;

    /// <summary>Asks the task view to copy the top selected cell's value down the selection in its current column.</summary>
    public event EventHandler? FillDownRequested;

    private void InitializeEditingCommands()
    {
        FindCommand = new RelayCommand(() => ShowDialog(new FindReplaceDialog(this, replace: false)));
        ReplaceCommand = new RelayCommand(() => ShowDialog(new FindReplaceDialog(this, replace: true)));
        SpellingCommand = new RelayCommand(() => ShowDialog(new SpellingDialog(this)));
        FillDownCommand = new RelayCommand(() => FillDownRequested?.Invoke(this, EventArgs.Empty), () => SelectedTasks.Count > 1);
    }

    public void SelectTask(ProjectTask task)
    {
        SetSelection(new[] { task });
        SelectTaskRequested?.Invoke(this, task);
    }
}
