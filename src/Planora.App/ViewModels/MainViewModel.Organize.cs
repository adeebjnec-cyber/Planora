using System.Windows;
using System.Windows.Input;
using Planora.App.Dialogs;
using Planora.App.Infrastructure;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.App.ViewModels;

/// <summary>Tables, filters, groups, sorting and saved views for the task sheet.</summary>
public sealed partial class MainViewModel
{
    private string _currentTable = "Entry";
    private string _currentFilter = "All Tasks";
    private string _currentGroup = "No Group";
    private SortDefinition _currentSort = new();

    /// <summary>Raised when the task sheet's columns should be rebuilt (table changed or redefined).</summary>
    public event EventHandler? TableChanged;

    public IReadOnlyList<string> TableNames => ViewCatalog.Tables(Project).Select(t => t.Name).ToList();
    public IReadOnlyList<string> FilterNames => ViewCatalog.Filters(Project).Select(f => f.Name).ToList();
    public IReadOnlyList<string> GroupNames => ViewCatalog.Groups(Project).Select(g => g.Name).ToList();
    public IReadOnlyList<string> SavedViewNames => Project.SavedViews.Select(v => v.Name).ToList();

    public TableDefinition CurrentTable => ViewCatalog.Table(Project, _currentTable);

    public string CurrentTableName
    {
        get => _currentTable;
        set
        {
            if (value is null || !SetProperty(ref _currentTable, value))
                return;
            TableChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string CurrentFilterName
    {
        get => _currentFilter;
        set
        {
            if (value is null || value == _currentFilter)
                return;
            // An interactive filter asks for its values first; cancelling keeps the previous filter.
            var filter = ViewCatalog.Filter(Project, value);
            if (filter.IsInteractive && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                var answers = FilterPromptDialog.Ask(filter);
                if (answers is null)
                {
                    OnPropertyChanged();
                    return;
                }
                _filterAnswers = answers;
            }
            if (SetProperty(ref _currentFilter, value))
                ReorganizeRows();
        }
    }

    private IReadOnlyDictionary<string, string> _filterAnswers = new Dictionary<string, string>();

    /// <summary>The current filter with the answers to its prompts filled in.</summary>
    internal FilterDefinition ActiveFilter()
    {
        var filter = ViewCatalog.Filter(Project, _currentFilter);
        return filter.IsInteractive ? filter.WithAnswers(_filterAnswers) : filter;
    }

    // ---- AutoFilter: per-column value lists chosen from the column headers ----

    private readonly Dictionary<string, HashSet<string>> _autoFilters = new();

    public bool HasAutoFilter => _autoFilters.Count > 0;

    public bool IsAutoFiltered(string fieldKey) => _autoFilters.ContainsKey(fieldKey);

    public IReadOnlySet<string>? AutoFilterValues(string fieldKey) => _autoFilters.TryGetValue(fieldKey, out var set) ? set : null;

    /// <summary>Shows only tasks whose field displays one of <paramref name="values"/>; null clears the column's AutoFilter.</summary>
    public void SetAutoFilter(string fieldKey, IEnumerable<string>? values)
    {
        if (values is null)
            _autoFilters.Remove(fieldKey);
        else
            _autoFilters[fieldKey] = values.ToHashSet();
        OnPropertyChanged(nameof(HasAutoFilter));
        ReorganizeRows();
    }

    public void ClearAutoFilters()
    {
        _autoFilters.Clear();
        OnPropertyChanged(nameof(HasAutoFilter));
        ReorganizeRows();
    }

    /// <summary>The text AutoFilter compares for a task (dates by day).</summary>
    public string AutoFilterText(ProjectTask task, string fieldKey)
    {
        if (Engine.Fields.TaskFields.Find(Project, fieldKey) is not { } field)
            return "";
        return field.Get(Project, task) is DateTime date ? DateText.FormatDay(DateOnly.FromDateTime(date)) : field.Format(Project, task);
    }

    private IEnumerable<ProjectTask> ApplyAutoFilters(IEnumerable<ProjectTask> tasks)
    {
        if (_autoFilters.Count == 0)
            return tasks;
        var list = tasks.ToList();
        var matching = list.Where(t => !t.IsSummary && _autoFilters.All(f => f.Value.Contains(AutoFilterText(t, f.Key)))).ToHashSet();
        // Keep the summaries above matching tasks, as Microsoft Project's AutoFilter does.
        foreach (var task in matching.ToList())
        {
            for (var parent = task.Parent; parent is not null; parent = parent.Parent)
                matching.Add(parent);
        }
        return list.Where(matching.Contains);
    }

    public string CurrentGroupName
    {
        get => _currentGroup;
        set
        {
            if (value is not null && SetProperty(ref _currentGroup, value))
                ReorganizeRows();
        }
    }

    public SortDefinition CurrentSort
    {
        get => _currentSort;
        set
        {
            _currentSort = value;
            OnPropertyChanged();
            ReorganizeRows();
        }
    }

    /// <summary>Whether rows are shown in outline order with indentation (no grouping; no flat sort).</summary>
    public bool ShowsOutline => ViewCatalog.Group(Project, _currentGroup).IsNoGroup && (CurrentSort.IsDefault || CurrentSort.KeepOutlineStructure);

    public ICommand SortCommand { get; private set; } = null!;
    public ICommand SortByIdCommand { get; private set; } = null!;
    public ICommand CustomFieldsCommand { get; private set; } = null!;
    public ICommand EditTableCommand { get; private set; } = null!;
    public ICommand EditFilterCommand { get; private set; } = null!;
    public ICommand EditGroupCommand { get; private set; } = null!;
    public ICommand SaveViewCommand { get; private set; } = null!;
    public ICommand ApplySavedViewCommand { get; private set; } = null!;

    private void InitializeOrganizeCommands()
    {
        SortCommand = new RelayCommand(() => ShowDialog(new SortDialog(this)));
        SortByIdCommand = new RelayCommand(() => CurrentSort = new SortDefinition(), () => !CurrentSort.IsDefault);
        CustomFieldsCommand = new RelayCommand(() => ShowDialog(new CustomFieldsDialog(this)));
        EditTableCommand = new RelayCommand(() => ShowDialog(new TableEditorDialog(this)));
        EditFilterCommand = new RelayCommand(() => ShowDialog(new FilterEditorDialog(this)));
        EditGroupCommand = new RelayCommand(() => ShowDialog(new GroupEditorDialog(this)));
        SaveViewCommand = new RelayCommand(SaveView);
        ApplySavedViewCommand = new RelayCommand(name => ApplySavedView(name as string));
    }

    private void NotifyDefinitionLists()
    {
        OnPropertyChanged(nameof(TableNames));
        OnPropertyChanged(nameof(FilterNames));
        OnPropertyChanged(nameof(GroupNames));
        OnPropertyChanged(nameof(SavedViewNames));
    }

    /// <summary>Called after table/filter/group definitions change, so lists, columns and rows refresh.</summary>
    internal void DefinitionsChanged()
    {
        NotifyDefinitionLists();
        TableChanged?.Invoke(this, EventArgs.Empty);
        ReorganizeRows();
    }

    private void ReorganizeRows()
    {
        SyncRows();
        UpdateStatus();
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Task rows to show, after filtering, sorting and grouping.</summary>
    private IEnumerable<(ProjectTask? Task, TaskGroup? Group, int Depth)> DisplayItems()
    {
        var p = Project;
        var filter = ActiveFilter();
        var group = ViewCatalog.Group(p, _currentGroup);
        IEnumerable<ProjectTask> tasks = filter.IsAllTasks || _highlightFilter ? p.Tasks : filter.Apply(p, p.Tasks);
        tasks = ApplyAutoFilters(tasks);

        if (group.IsNoGroup)
        {
            foreach (var task in CurrentSort.Apply(p, tasks))
            {
                if (!HiddenByOutline(task))
                    yield return (task, null, -1);
            }
            yield break;
        }

        var ordered = CurrentSort.IsDefault ? tasks.ToList()
            : new SortDefinition { Keys = CurrentSort.Keys, KeepOutlineStructure = false }.Apply(p, tasks);
        foreach (var item in Flatten(group.Apply(p, ordered)))
            yield return item;

        static IEnumerable<(ProjectTask?, TaskGroup?, int)> Flatten(IEnumerable<TaskGroup> groups)
        {
            foreach (var g in groups)
            {
                yield return (null, g, g.Level);
                foreach (var nested in Flatten(g.Subgroups))
                    yield return nested;
                foreach (var t in g.Tasks)
                    yield return (t, null, g.Level);
            }
        }
    }

    private void SaveView()
    {
        var name = InputDialog.Ask("Save View", "Name for this view (screen, table, filter, group and sort):",
            $"{CurrentViewOption.Title} - {_currentTable}");
        if (string.IsNullOrWhiteSpace(name))
            return;
        var view = new SavedView
        {
            Name = name.Trim(),
            Screen = CurrentView.ToString(),
            Table = _currentTable,
            Filter = _currentFilter,
            Group = _currentGroup,
            Sort = new SortDefinition { Keys = CurrentSort.Keys.ToList(), KeepOutlineStructure = CurrentSort.KeepOutlineStructure },
        };
        if (TryEdit("Save view", p =>
            {
                p.SavedViews.RemoveAll(v => v.Name == view.Name);
                p.SavedViews.Add(view);
            }))
            DefinitionsChanged();
    }

    private void ApplySavedView(string? name)
    {
        if (Project.SavedViews.FirstOrDefault(v => v.Name == name) is not { } view)
            return;
        if (Enum.TryParse<ViewKind>(view.Screen, out var screen))
            CurrentView = screen;
        _currentTable = view.Table;
        _currentFilter = view.Filter;
        _currentGroup = view.Group;
        _currentSort = new SortDefinition { Keys = view.Sort.Keys.ToList(), KeepOutlineStructure = view.Sort.KeepOutlineStructure };
        OnPropertyChanged(nameof(CurrentTableName));
        OnPropertyChanged(nameof(CurrentFilterName));
        OnPropertyChanged(nameof(CurrentGroupName));
        OnPropertyChanged(nameof(CurrentSort));
        TableChanged?.Invoke(this, EventArgs.Empty);
        ReorganizeRows();
    }
}
