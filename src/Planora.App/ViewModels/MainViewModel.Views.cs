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
}

/// <summary>An entry in the view bar.</summary>
public sealed record ViewOption(ViewKind Kind, string Title, string Glyph);

/// <summary>Views and the Resource Sheet.</summary>
public sealed partial class MainViewModel
{
    private ViewKind _currentView = ViewKind.Gantt;

    public IReadOnlyList<ViewOption> Views { get; } = new[]
    {
        new ViewOption(ViewKind.Gantt, "Gantt Chart", ""),
        new ViewOption(ViewKind.TrackingGantt, "Tracking Gantt", ""),
        new ViewOption(ViewKind.NetworkDiagram, "Network Diagram", ""),
        new ViewOption(ViewKind.Calendar, "Calendar", ""),
        new ViewOption(ViewKind.TaskBoard, "Task Board", ""),
        new ViewOption(ViewKind.Timeline, "Timeline", ""),
        new ViewOption(ViewKind.TaskUsage, "Task Usage", ""),
        new ViewOption(ViewKind.ResourceSheet, "Resource Sheet", ""),
        new ViewOption(ViewKind.ResourceUsage, "Resource Usage", ""),
    };

    public ViewKind CurrentView
    {
        get => _currentView;
        set
        {
            if (!SetProperty(ref _currentView, value))
                return;
            OnPropertyChanged(nameof(CurrentViewOption));
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

    private void InitializeViewCommands()
    {
        DeleteResourceCommand = new RelayCommand(DeleteResources, () => SelectedResources.Count > 0);
    }

    public void SetResourceSelection(IEnumerable<Resource> resources)
    {
        SelectedResources.Clear();
        SelectedResources.AddRange(resources);
    }

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
