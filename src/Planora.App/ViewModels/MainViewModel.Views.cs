using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Planora.App.Infrastructure;
using Planora.Engine.Model;

namespace Planora.App.ViewModels;

public enum ViewKind
{
    Gantt,
    TrackingGantt,
    NetworkDiagram,
    Calendar,
    TaskBoard,
    Timeline,
    TaskUsage,
    ResourceSheet,
    ResourceUsage,
    TaskSheet,
    ResourceGraph,
    TeamPlanner,
    RelationshipDiagram,
}

/// <summary>An entry in the sidebar: the view, its name, its line icon and the sidebar section it sits in.</summary>
public sealed record ViewOption(ViewKind Kind, string Title, string Icon, string Section);

/// <summary>Views and the Resource Sheet.</summary>
public sealed partial class MainViewModel
{
    private ViewKind _currentView = ViewKind.Gantt;

    public IReadOnlyList<ViewOption> Views { get; } = new[]
    {
        new ViewOption(ViewKind.Gantt, "Gantt Chart", "gantt", "Schedule"),
        new ViewOption(ViewKind.TrackingGantt, "Tracking Gantt", "tracking", "Schedule"),
        new ViewOption(ViewKind.NetworkDiagram, "Network Diagram", "network", "Schedule"),
        new ViewOption(ViewKind.Calendar, "Calendar", "calendar", "Schedule"),
        new ViewOption(ViewKind.TaskBoard, "Kanban Board", "kanban", "Schedule"),
        new ViewOption(ViewKind.Timeline, "Timeline", "timeline", "Schedule"),
        new ViewOption(ViewKind.TaskUsage, "Task Usage", "taskusage", "Schedule"),
        new ViewOption(ViewKind.TaskSheet, "Task Sheet", "tasksheet", "Schedule"),
        new ViewOption(ViewKind.RelationshipDiagram, "Relationship Diagram", "relations", "Schedule"),
        new ViewOption(ViewKind.ResourceSheet, "Resource Sheet", "resources", "Resources"),
        new ViewOption(ViewKind.ResourceUsage, "Resource Usage", "resusage", "Resources"),
        new ViewOption(ViewKind.ResourceGraph, "Resource Graph", "resgraph", "Resources"),
        new ViewOption(ViewKind.TeamPlanner, "Team Planner", "team", "Resources"),
    };

    private bool _showTaskForm;
    private bool _showTimelinePane;
    private bool _showInspector;

    /// <summary>Task > Inspect: the Task Inspector pane.</summary>
    public bool ShowInspector
    {
        get => _showInspector;
        set => SetProperty(ref _showInspector, value);
    }

    /// <summary>View > Timeline: the Timeline above the current view.</summary>
    public bool ShowTimelinePane
    {
        get => _showTimelinePane;
        set => SetProperty(ref _showTimelinePane, value);
    }

    /// <summary>View > Details: the Task Form under the current view.</summary>
    public bool ShowTaskForm
    {
        get => _showTaskForm;
        set
        {
            if (SetProperty(ref _showTaskForm, value))
                NotifyDetailsPanes();
        }
    }

    private void NotifyDetailsPanes()
    {
        OnPropertyChanged(nameof(ShowResourceForm));
        OnPropertyChanged(nameof(ShowTaskDetailsForm));
    }

    public ViewKind CurrentView
    {
        get => _currentView;
        set
        {
            if (!SetProperty(ref _currentView, value))
                return;
            OnPropertyChanged(nameof(CurrentViewOption));
            NotifyDetailsPanes();
            // The Tracking Gantt is the Gantt Chart with baselines shown.
            if (value == ViewKind.TrackingGantt)
                ShowBaseline = true;
            else if (value == ViewKind.Gantt)
                ShowBaseline = false;
        }
    }

    public ViewOption CurrentViewOption
    {
        get => Views.First(v => v.Kind == CurrentView);
        set => CurrentView = value.Kind;
    }

    /// <summary>Resource Sheet rows plus a trailing blank row for typing new resources.</summary>
    public ObservableCollection<ResourceRowViewModel> ResourceRows { get; } = new();

    public List<Resource> SelectedResources { get; } = new();

    public ICommand DeleteResourceCommand { get; private set; } = null!;

    public ICommand AddToTimelineCommand { get; private set; } = null!;

    private void InitializeViewCommands()
    {
        DeleteResourceCommand = new RelayCommand(DeleteResources, () => SelectedResources.Count > 0);
        AddToTimelineCommand = new RelayCommand(ToggleTimeline, () => SelectedTasks.Count > 0);
    }

    /// <summary>Adds the selected tasks to the Timeline, or removes them if they are all already on it.</summary>
    private void ToggleTimeline()
    {
        var ids = SelectedTasks.Select(t => t.Id).ToList();
        bool add = SelectedTasks.Any(t => !t.ShowOnTimeline);
        TryEdit(add ? "Add to timeline" : "Remove from timeline", p =>
        {
            foreach (var id in ids)
                p.GetRequiredTask(id).ShowOnTimeline = add;
        });
    }

    /// <summary>Raised when the resource selection changes (the Resource Form follows it).</summary>
    public event EventHandler? ResourceSelectionChanged;

    public void SetResourceSelection(IEnumerable<Resource> resources)
    {
        SelectedResources.Clear();
        SelectedResources.AddRange(resources);
        ResourceSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The Details pane shows the Resource Form under resource views and the Task Form elsewhere.</summary>
    public bool ShowResourceForm => ShowTaskForm && CurrentView is ViewKind.ResourceSheet or ViewKind.ResourceUsage or ViewKind.ResourceGraph;

    public bool ShowTaskDetailsForm => ShowTaskForm && !ShowResourceForm;

    internal void AddResourceFromPlaceholder(string name)
    {
        Application.Current.Dispatcher.BeginInvoke(() => TryEdit("New resource", p => p.AddResource(name)));
    }

    private void DeleteResources()
    {
        var ids = SelectedResources.Select(r => r.Id).ToList();
        int assignments = SelectedResources.Sum(r => Project.AssignmentsOf(r).Count());
        if (assignments > 0 && MessageBox.Show(Application.Current.MainWindow,
                $"The selected resources have {assignments} assignment(s), which will also be removed. Continue?",
                "Planora", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        TryEdit(ids.Count == 1 ? "Delete resource" : "Delete resources", p =>
        {
            foreach (var id in ids)
            {
                if (p.GetResource(id) is { } r)
                    p.RemoveResource(r);
            }
        });
    }

    private void SyncResourceRows()
    {
        var resources = Project.Resources;
        var existing = ResourceRows.Where(r => r.Resource is not null).ToDictionary(r => r.Resource!.Id);
        var placeholder = ResourceRows.FirstOrDefault(r => r.IsPlaceholder) ?? new ResourceRowViewModel(this, null);

        var wanted = new List<ResourceRowViewModel>(resources.Count + 1);
        foreach (var resource in resources)
        {
            if (existing.TryGetValue(resource.Id, out var row))
                row.Rebind(resource);
            else
                row = new ResourceRowViewModel(this, resource);
            wanted.Add(row);
        }
        placeholder.Refresh();
        wanted.Add(placeholder);

        for (int i = ResourceRows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(ResourceRows[i]))
                ResourceRows.RemoveAt(i);
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            int current = ResourceRows.IndexOf(wanted[i]);
            if (current < 0)
                ResourceRows.Insert(i, wanted[i]);
            else if (current != i)
                ResourceRows.Move(current, i);
        }

        var selectedIds = SelectedResources.Select(r => r.Id).ToList();
        SelectedResources.Clear();
        SelectedResources.AddRange(selectedIds.Select(Project.GetResource).OfType<Resource>());
    }
}
