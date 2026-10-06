using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Planora.App.Documents;
using Planora.App.Infrastructure;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Gantt zoom steps in pixels per day, from years down to hours.</summary>
    public static readonly double[] ZoomLevels = { 0.5, 1.5, 4, 10, 24, 48, 120, 300 };

    private const string FileFilter = "Planora project (*.planora)|*.planora|All files (*.*)|*.*";

    private readonly DispatcherTimer _autosaveTimer;
    private ProjectDocument _document;
    private string _status = "Ready";
    private int _zoomIndex = 4;
    private bool _showCriticalPath = true;
    private bool _showBaseline;

    public MainViewModel()
    {
        _document = ProjectDocument.CreateNew();
        _document.Changed += OnDocumentChanged;
        SyncRows();

        NewCommand = new RelayCommand(New);
        OpenCommand = new RelayCommand(Open);
        OpenSampleCommand = new RelayCommand(LoadSample);
        SaveCommand = new RelayCommand(() => Save());
        SaveAsCommand = new RelayCommand(() => SaveAs());
        UndoCommand = new RelayCommand(() => Document.Undo(), () => Document.CanUndo);
        RedoCommand = new RelayCommand(() => Document.Redo(), () => Document.CanRedo);
        InsertTaskCommand = new RelayCommand(InsertTask);
        DeleteTaskCommand = new RelayCommand(DeleteTasks, () => SelectedTasks.Count > 0);
        IndentCommand = new RelayCommand(() => Indent(true), () => SelectedTasks.Count > 0);
        OutdentCommand = new RelayCommand(() => Indent(false), () => SelectedTasks.Any(t => t.OutlineLevel > 1));
        LinkCommand = new RelayCommand(LinkSelected, () => SelectedTasks.Count > 1);
        UnlinkCommand = new RelayCommand(UnlinkSelected, () => SelectedTasks.Count > 0);
        InsertMilestoneCommand = new RelayCommand(InsertMilestone);
        InsertSummaryCommand = new RelayCommand(InsertSummary);
        ToggleModeCommand = new RelayCommand(ToggleMode, () => SelectedTasks.Any(t => !t.IsSummary));
        ZoomInCommand = new RelayCommand(() => ZoomIndex++, () => ZoomIndex < ZoomLevels.Length - 1);
        ZoomOutCommand = new RelayCommand(() => ZoomIndex--, () => ZoomIndex > 0);
        ScrollToTaskCommand = new RelayCommand(() => ScrollToTaskRequested?.Invoke(this, EventArgs.Empty), () => SelectedTasks.Count > 0);
        InitializeViewCommands();
        InitializeDialogCommands();

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _autosaveTimer.Tick += (_, _) => Autosave();
        _autosaveTimer.Start();
    }

    /// <summary>Raised after every change, so custom-drawn views (the Gantt chart) repaint.</summary>
    public event EventHandler? Refreshed;

    /// <summary>Asks the view to scroll the Gantt chart to the selected task.</summary>
    public event EventHandler? ScrollToTaskRequested;

    public ProjectDocument Document
    {
        get => _document;
        private set
        {
            _document.Changed -= OnDocumentChanged;
            _document = value;
            _document.Changed += OnDocumentChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            SyncRows();
            UpdateStatus();
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Project Project => Document.Project;

    /// <summary>Task rows plus a trailing blank row for typing new tasks.</summary>
    public ObservableCollection<TaskRowViewModel> Rows { get; } = new();

    /// <summary>Selected tasks in row order, kept in step with the task sheet's selection.</summary>
    public List<ProjectTask> SelectedTasks { get; } = new();

    public string WindowTitle => $"{Document.Title} - Planora";

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public int ZoomIndex
    {
        get => _zoomIndex;
        set
        {
            if (SetProperty(ref _zoomIndex, Math.Clamp(value, 0, ZoomLevels.Length - 1)))
            {
                OnPropertyChanged(nameof(PixelsPerDay));
                Refreshed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public double PixelsPerDay => ZoomLevels[ZoomIndex];

    public bool ShowCriticalPath
    {
        get => _showCriticalPath;
        set
        {
            if (SetProperty(ref _showCriticalPath, value))
                Refreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowBaseline
    {
        get => _showBaseline;
        set
        {
            if (SetProperty(ref _showBaseline, value))
                Refreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand OpenSampleCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand InsertTaskCommand { get; }
    public ICommand InsertMilestoneCommand { get; }
    public ICommand InsertSummaryCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand IndentCommand { get; }
    public ICommand OutdentCommand { get; }
    public ICommand LinkCommand { get; }
    public ICommand UnlinkCommand { get; }
    public ICommand ToggleModeCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ScrollToTaskCommand { get; }

    // ------------------------------------------------------------------ editing

    /// <summary>Applies an undoable change; on failure shows why and leaves the project unchanged.</summary>
    public bool TryEdit(string description, Action<Project> change)
    {
        try
        {
            Document.Edit(description, change);
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return false;
        }
    }

    public void ShowError(string message)
    {
        Status = message;
        MessageBox.Show(Application.Current.MainWindow, message, "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void SetSelection(IEnumerable<ProjectTask> tasks)
    {
        SelectedTasks.Clear();
        SelectedTasks.AddRange(tasks.OrderBy(Project.IndexOf));
        Refreshed?.Invoke(this, EventArgs.Empty);
        UpdateStatus();
    }

    internal void AddTaskFromPlaceholder(string name)
    {
        // Defer so the sheet finishes committing the placeholder cell before rows change.
        Application.Current.Dispatcher.BeginInvoke(() =>
            TryEdit("New task", p => p.AddTask(name, p.Options.MinutesPerDay).IsEstimated = true));
    }

    private void InsertTask() => InsertAtSelection("Insert task", (p, t) => { t.DurationMinutes = p.Options.MinutesPerDay; t.IsEstimated = true; });

    private void InsertMilestone() => InsertAtSelection("Insert milestone", (_, t) => { t.Name = "<New Milestone>"; t.IsMilestone = true; });

    /// <summary>Inserts a summary task above the selection and indents the selected tasks under it.</summary>
    private void InsertSummary()
    {
        var ids = SelectedTasks.Select(t => t.Id).ToList();
        if (ids.Count == 0)
        {
            InsertAtSelection("Insert summary task", (_, t) => t.Name = "<New Summary Task>");
            return;
        }
        TryEdit("Insert summary task", p =>
        {
            var tasks = ids.Select(p.GetRequiredTask).ToList();
            var summary = p.InsertTask(p.IndexOf(tasks[0]), "<New Summary Task>");
            foreach (var t in tasks.Where(t => !tasks.Contains(t.Parent!)))
                p.Indent(t);
        });
    }

    private void InsertAtSelection(string description, Action<Project, ProjectTask> setup)
    {
        int index = SelectedTasks.Count > 0 ? Project.IndexOf(SelectedTasks[0]) : Project.Tasks.Count;
        TryEdit(description, p => setup(p, p.InsertTask(index, "<New Task>")));
    }

    private void DeleteTasks()
    {
        var ids = SelectedTasks.Select(t => t.Id).ToList();
        TryEdit(ids.Count == 1 ? "Delete task" : "Delete tasks", p =>
        {
            foreach (var id in ids)
            {
                if (p.GetTask(id) is { } task)
                    p.RemoveTask(task);
            }
        });
    }

    private void Indent(bool indent)
    {
        var ids = SelectedTasks.Select(t => t.Id).ToList();
        TryEdit(indent ? "Indent" : "Outdent", p =>
        {
            var tasks = ids.Select(p.GetRequiredTask).ToList();
            // Move whole subtrees once: skip tasks whose parent is also selected.
            foreach (var t in tasks.Where(t => !tasks.Contains(t.Parent!)))
            {
                if (indent)
                    p.Indent(t);
                else
                    p.Outdent(t);
            }
        });
    }

    /// <summary>Links the selected tasks finish-to-start in row order, like Microsoft Project's Link Tasks.</summary>
    private void LinkSelected()
    {
        var ids = SelectedTasks.Select(t => t.Id).ToList();
        TryEdit("Link tasks", p =>
        {
            for (int i = 1; i < ids.Count; i++)
            {
                var pred = p.GetRequiredTask(ids[i - 1]);
                var succ = p.GetRequiredTask(ids[i]);
                if (!succ.Predecessors.Any(l => l.PredecessorId == pred.Id) && !Project.IsAncestor(pred, succ) && !Project.IsAncestor(succ, pred))
                    p.Link(pred, succ);
            }
        });
    }

    /// <summary>Removes links among the selected tasks, or all links of a single selected task.</summary>
    private void UnlinkSelected()
    {
        var ids = SelectedTasks.Select(t => t.Id).ToHashSet();
        TryEdit("Unlink tasks", p =>
        {
            foreach (var task in p.Tasks)
            {
                if (ids.Count == 1)
                {
                    if (ids.Contains(task.Id))
                        task.Predecessors.Clear();
                    else
                        task.Predecessors.RemoveAll(l => ids.Contains(l.PredecessorId));
                }
                else if (ids.Contains(task.Id))
                {
                    task.Predecessors.RemoveAll(l => ids.Contains(l.PredecessorId));
                }
            }
        });
    }

    private void ToggleMode()
    {
        var ids = SelectedTasks.Where(t => !t.IsSummary).Select(t => t.Id).ToList();
        bool toManual = SelectedTasks.Any(t => !t.IsSummary && t.Mode == TaskMode.AutoScheduled);
        TryEdit(toManual ? "Manually schedule" : "Auto schedule", p =>
        {
            foreach (var t in ids.Select(p.GetRequiredTask))
            {
                if (toManual)
                {
                    t.ManualStart = t.Start;
                    t.ManualFinish = t.Finish;
                }
                t.Mode = toManual ? TaskMode.ManuallyScheduled : TaskMode.AutoScheduled;
            }
        });
    }

    // ---- Edits from the Gantt chart ----

    public void MoveTask(ProjectTask task, DateTime newStart)
    {
        int id = task.Id;
        TryEdit("Move task", p =>
        {
            var t = p.GetRequiredTask(id);
            if (t.Mode == TaskMode.ManuallyScheduled)
            {
                var cal = t.Calendar ?? p.Calendar;
                t.ManualStart = cal.NextWorkingMoment(newStart);
                t.ManualFinish = cal.AddWorkingMinutes(t.ManualStart.Value, t.DurationMinutes);
            }
            else
            {
                t.ConstraintType = ConstraintType.StartNoEarlierThan;
                t.ConstraintDate = newStart;
            }
        });
    }

    public void ResizeTask(ProjectTask task, long durationMinutes)
    {
        int id = task.Id;
        TryEdit("Change duration", p =>
        {
            var t = p.GetRequiredTask(id);
            p.SetDuration(t, Math.Max(0, durationMinutes));
            if (t.Mode == TaskMode.ManuallyScheduled)
                t.ManualFinish = (t.Calendar ?? p.Calendar).AddWorkingMinutes(t.Start, t.DurationMinutes);
        });
    }

    public void SetProgress(ProjectTask task, double percent)
    {
        int id = task.Id;
        TryEdit("Update progress", p => TaskRowViewModel.SetProgress(p.GetRequiredTask(id), Math.Round(Math.Clamp(percent, 0, 100))));
    }

    public void LinkTasks(ProjectTask predecessor, ProjectTask successor)
    {
        var (predId, succId) = (predecessor.Id, successor.Id);
        TryEdit("Link tasks", p => p.Link(p.GetRequiredTask(predId), p.GetRequiredTask(succId)));
    }

    // ------------------------------------------------------------------ files

    private void New()
    {
        if (ConfirmDiscard())
            Document = ProjectDocument.CreateNew();
    }

    private void Open()
    {
        if (!ConfirmDiscard())
            return;
        var dialog = new OpenFileDialog { Filter = FileFilter, DefaultExt = ProjectFile.Extension };
        if (dialog.ShowDialog() == true)
            OpenFile(dialog.FileName);
    }

    /// <summary>Opens the built-in sample project, starting the Monday before last so it shows progress.</summary>
    public void LoadSample()
    {
        if (!ConfirmDiscard())
            return;
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 7).AddHours(8);
        Document = new ProjectDocument(SampleProject.Create(monday));
        Status = "Opened the sample project";
    }

    public void OpenFile(string path)
    {
        try
        {
            Document = ProjectDocument.Open(path);
            Status = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowError($"Could not open the project: {ex.Message}");
        }
    }

    public bool Save()
    {
        if (Document.FilePath is null)
            return SaveAs();
        return SaveTo(Document.FilePath);
    }

    public bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = FileFilter,
            DefaultExt = ProjectFile.Extension,
            FileName = Document.FilePath is null ? Project.Name : Path.GetFileName(Document.FilePath),
        };
        return dialog.ShowDialog() == true && SaveTo(dialog.FileName);
    }

    private bool SaveTo(string path)
    {
        try
        {
            Document.Save(path);
            Status = $"Saved {Path.GetFileName(path)}";
            OnPropertyChanged(nameof(WindowTitle));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save the project: {ex.Message}");
            return false;
        }
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancels.</summary>
    public bool ConfirmDiscard()
    {
        if (!Document.IsDirty)
            return true;
        var answer = MessageBox.Show(Application.Current.MainWindow,
            $"Do you want to save changes to '{Document.Title.TrimEnd(' ', '*')}'?", "Planora",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
            return false;
        if (answer == MessageBoxResult.Yes)
            return Save();
        Document.DeleteAutosave();
        return true;
    }

    private void Autosave()
    {
        try
        {
            Document.Autosave();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Autosave failed: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------ refresh

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        SyncRows();
        UpdateStatus();
        OnPropertyChanged(nameof(WindowTitle));
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Brings the rows in line with the project's tasks, reusing row objects by task ID so the
    /// sheet keeps its scroll position and editing state.
    /// </summary>
    private void SyncRows()
    {
        var tasks = Project.Tasks;
        var existing = Rows.Where(r => r.Task is not null).ToDictionary(r => r.Task!.Id);
        var placeholder = Rows.FirstOrDefault(r => r.IsPlaceholder) ?? new TaskRowViewModel(this, null);

        var wanted = new List<TaskRowViewModel>(tasks.Count + 1);
        foreach (var task in tasks)
        {
            if (existing.TryGetValue(task.Id, out var row))
                row.Rebind(task);
            else
                row = new TaskRowViewModel(this, task);
            wanted.Add(row);
        }
        placeholder.Refresh();
        wanted.Add(placeholder);

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Rows[i]))
                Rows.RemoveAt(i);
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            int current = Rows.IndexOf(wanted[i]);
            if (current < 0)
                Rows.Insert(i, wanted[i]);
            else if (current != i)
                Rows.Move(current, i);
        }

        SyncResourceRows();

        // Keep the selection pointing at the current task objects (undo replaces them).
        var selectedIds = SelectedTasks.Select(t => t.Id).ToList();
        SelectedTasks.Clear();
        SelectedTasks.AddRange(selectedIds.Select(Project.GetTask).OfType<ProjectTask>());
    }

    private void UpdateStatus()
    {
        if (Document.ScheduleError is { } error)
        {
            Status = error;
            return;
        }
        var p = Project;
        var finish = p.Tasks.Count == 0 ? "" : $"   Finish: {TaskRowViewModel.FormatDate(p.Finish)}";
        var selected = SelectedTasks.Count > 0 ? $"   Selected: {SelectedTasks.Count}" : "";
        Status = $"Tasks: {p.Tasks.Count}{finish}   Cost: {p.Cost:C}{selected}";
    }
}
