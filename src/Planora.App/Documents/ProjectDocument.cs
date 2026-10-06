using System.IO;
using Planora.App.Infrastructure;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.App.Documents;

/// <summary>
/// An open project: the engine model plus file path, unsaved-changes tracking and undo/redo.
/// Every change goes through <see cref="Edit"/>, which snapshots the project, applies the change,
/// reschedules, and rolls back if the change is invalid. Snapshots use the project file format,
/// so undo restores exactly what a save/load would.
/// </summary>
public sealed class ProjectDocument : ObservableObject
{
    private const int MaxUndoSteps = 100;

    private readonly LinkedList<(string Description, string Snapshot)> _undo = new();
    private readonly Stack<(string Description, string Snapshot)> _redo = new();
    private string? _filePath;
    private bool _isDirty;

    public ProjectDocument(Project project, string? filePath = null)
    {
        Project = project;
        _filePath = filePath;
        Reschedule();
    }

    /// <summary>Raised after any change (edit, undo, redo, reload) so views refresh.</summary>
    public event EventHandler? Changed;

    public Project Project { get; private set; }

    /// <summary>The last scheduling problem (e.g. a dependency loop), or null when the schedule is valid.</summary>
    public string? ScheduleError { get; private set; }

    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (SetProperty(ref _filePath, value))
                OnPropertyChanged(nameof(Title));
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                OnPropertyChanged(nameof(Title));
        }
    }

    public string Title => (FilePath is null ? Project.Name : Path.GetFileNameWithoutExtension(FilePath)) + (IsDirty ? " *" : "");

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Last?.Value.Description;
    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    public static ProjectDocument CreateNew()
    {
        var start = DateTime.Today.AddHours(8);
        var project = new Project("Project1", start);
        project.Calendars.Add(Engine.Calendars.WorkingCalendar.Create24Hours());
        project.Calendars.Add(Engine.Calendars.WorkingCalendar.CreateNightShift());
        return new ProjectDocument(project);
    }

    public static ProjectDocument Open(string path) => new(ProjectFile.Load(path), path);

    /// <summary>
    /// Applies a change as one undoable step. If the change throws, or leaves the project impossible
    /// to schedule (e.g. a dependency loop), the project is restored and the error is rethrown.
    /// </summary>
    public void Edit(string description, Action<Project> change)
    {
        var before = ProjectFile.Serialize(Project);
        try
        {
            change(Project);
            ProjectScheduler.Schedule(Project);
        }
        catch
        {
            Project = ProjectFile.Deserialize(before);
            Reschedule();
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }

        _undo.AddLast((description, before));
        if (_undo.Count > MaxUndoSteps)
            _undo.RemoveFirst();
        _redo.Clear();
        ScheduleError = null;
        IsDirty = true;
        RaiseChanged();
    }

    public void Undo()
    {
        if (_undo.Last is not { } last)
            return;
        _undo.RemoveLast();
        _redo.Push((last.Value.Description, ProjectFile.Serialize(Project)));
        Restore(last.Value.Snapshot);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        var (description, snapshot) = _redo.Pop();
        _undo.AddLast((description, ProjectFile.Serialize(Project)));
        Restore(snapshot);
    }

    public void Save(string? path = null)
    {
        path ??= FilePath ?? throw new InvalidOperationException("Choose where to save the project first.");
        ProjectFile.Save(Project, path);
        FilePath = path;
        IsDirty = false;
        DeleteAutosave();
    }

    // ---- Autosave: a recovery copy next to the file (or in AppData for unsaved projects) ----

    public string AutosavePath => FilePath is not null
        ? Path.Combine(Path.GetDirectoryName(FilePath)!, "~" + Path.GetFileName(FilePath) + ".autosave")
        : Path.Combine(AutosaveFolder, $"Unsaved-{Project.Name}-{GetHashCode():x}{ProjectFile.Extension}");

    public static string AutosaveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Planora", "Autosave");

    public void Autosave()
    {
        if (!IsDirty)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(AutosavePath)!);
        ProjectFile.Save(Project, AutosavePath);
    }

    public void DeleteAutosave()
    {
        try
        {
            if (File.Exists(AutosavePath))
                File.Delete(AutosavePath);
        }
        catch (IOException)
        {
            // A stale recovery file is harmless.
        }
    }

    private void Restore(string snapshot)
    {
        Project = ProjectFile.Deserialize(snapshot);
        Reschedule();
        IsDirty = true;
        RaiseChanged();
    }

    private void Reschedule()
    {
        try
        {
            ProjectScheduler.Schedule(Project);
            ScheduleError = null;
        }
        catch (SchedulingException ex)
        {
            ScheduleError = ex.Message;
        }
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));
        OnPropertyChanged(nameof(Title));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
