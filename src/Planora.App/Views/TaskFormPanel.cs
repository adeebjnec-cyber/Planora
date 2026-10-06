using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Planora.App.Controls;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.App.Views;

/// <summary>
/// Task Form (View > Details): the selected task's name, duration, dates and % complete, with its
/// resources and predecessors, shown under the current view. Edits apply with OK.
/// </summary>
public sealed class TaskFormPanel : UserControl
{
    private readonly TextBox _name = Box(260), _duration = Box(90), _start = Box(130), _finish = Box(130), _percent = Box(60);
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _resources = NewGrid();
    private readonly DataGrid _predecessors = NewGrid();
    private readonly Button _ok = new() { Content = "OK", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(6, 0, 0, 0) };
    private readonly Button _info = new() { Content = "Task Information...", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
    private MainViewModel? _viewModel;
    private int? _taskId;

    public TaskFormPanel()
    {
        Background = Controls.Drawing.HeaderBackground;
        BorderBrush = Brushes.LightGray;
        BorderThickness = new Thickness(0, 1, 0, 0);

        _resources.Columns.Add(new DataGridTextColumn { Header = "Resource Name", Binding = new System.Windows.Data.Binding("Name"), Width = 170 });
        _resources.Columns.Add(new DataGridTextColumn { Header = "Units", Binding = new System.Windows.Data.Binding("Units"), Width = 60 });
        _resources.Columns.Add(new DataGridTextColumn { Header = "Work", Binding = new System.Windows.Data.Binding("Work"), Width = 70 });
        _predecessors.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new System.Windows.Data.Binding("Id"), Width = 40 });
        _predecessors.Columns.Add(new DataGridTextColumn { Header = "Predecessor Name", Binding = new System.Windows.Data.Binding("Name"), Width = 190 });
        _predecessors.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new System.Windows.Data.Binding("Type"), Width = 45 });
        _predecessors.Columns.Add(new DataGridTextColumn { Header = "Lag", Binding = new System.Windows.Data.Binding("Lag"), Width = 60 });

        _ok.Click += (_, _) => Apply();
        _info.Click += (_, _) => _viewModel?.TaskInformationCommand.Execute(null);

        var top = new WrapPanel { Margin = new Thickness(8, 6, 8, 4) };
        top.Children.Add(Label("Name:"));
        top.Children.Add(_name);
        top.Children.Add(Label("Duration:"));
        top.Children.Add(_duration);
        top.Children.Add(Label("% Complete:"));
        top.Children.Add(_percent);
        top.Children.Add(_ok);
        top.Children.Add(_info);
        var dates = new WrapPanel { Margin = new Thickness(8, 0, 8, 4) };
        dates.Children.Add(Label("Start:"));
        dates.Children.Add(_start);
        dates.Children.Add(Label("Finish:"));
        dates.Children.Add(_finish);
        dates.Children.Add(_title);

        var lists = new Grid { Margin = new Thickness(8, 2, 8, 8) };
        lists.ColumnDefinitions.Add(new ColumnDefinition());
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        lists.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_predecessors, 2);
        lists.Children.Add(_resources);
        lists.Children.Add(_predecessors);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(dates, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(dates);
        root.Children.Add(lists);
        Content = root;

        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        IsVisibleChanged += (_, _) => Load();
    }

    private static TextBox Box(double width) => new() { Width = width, Margin = new Thickness(0, 1, 12, 1), Padding = new Thickness(2, 1, 2, 1) };

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

    private static DataGrid NewGrid() => new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        RowHeight = 22,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        Background = Brushes.White,
    };

    private void Attach(MainViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.Refreshed -= OnRefreshed;
        _viewModel = viewModel;
        if (viewModel is not null)
            viewModel.Refreshed += OnRefreshed;
        Load();
    }

    private void OnRefreshed(object? sender, EventArgs e)
    {
        if (IsVisible)
            Load();
    }

    /// <summary>Shows the first selected task.</summary>
    private void Load()
    {
        var project = _viewModel?.Project;
        var task = _viewModel?.SelectedTasks.FirstOrDefault();
        IsEnabled = task is not null;
        _taskId = task?.Id;
        if (project is null || task is null)
        {
            _name.Text = _duration.Text = _start.Text = _finish.Text = _percent.Text = "";
            _title.Text = "  Select a task to see its details.";
            _resources.ItemsSource = null;
            _predecessors.ItemsSource = null;
            return;
        }
        var options = project.Options;
        _name.Text = task.Name;
        _duration.Text = DurationText.Format(task.DurationMinutes, options, isElapsed: task.IsElapsedDuration, isEstimated: task.IsEstimated);
        _start.Text = DateText.Format(task.Start);
        _finish.Text = DateText.Format(task.Finish);
        _percent.Text = task.PercentComplete.ToString("0.#", CultureInfo.CurrentCulture) + "%";
        _title.Text = $"   Task {project.IndexOf(task) + 1}{(task.IsSummary ? " (summary)" : "")}{(task.IsCritical ? ", critical" : "")}";
        bool leaf = !task.IsSummary;
        _duration.IsEnabled = _start.IsEnabled = _finish.IsEnabled = _percent.IsEnabled = leaf;
        _resources.ItemsSource = task.Assignments.Select(a => new
        {
            a.Resource.Name,
            Units = a.Resource.Type == ResourceType.Work ? $"{a.Units * 100:0}%" : a.Units.ToString("0.##", CultureInfo.CurrentCulture),
            Work = a.IsWork ? DurationText.Format(a.WorkMinutes, options, DurationUnit.Hours) : "",
        }).ToList();
        _predecessors.ItemsSource = task.Predecessors
            .Select(l => (Link: l, Task: project.GetTask(l.PredecessorId)))
            .Where(x => x.Task is not null)
            .Select(x => new
            {
                Id = project.IndexOf(x.Task!) + 1,
                x.Task!.Name,
                Type = x.Link.Type switch { LinkType.StartToStart => "SS", LinkType.FinishToFinish => "FF", LinkType.StartToFinish => "SF", _ => "FS" },
                Lag = DurationText.FormatLag(x.Link, options),
            }).ToList();
    }

    private void Apply()
    {
        if (_viewModel is null || _taskId is not { } id)
            return;
        var project = _viewModel.Project;
        var task = project.GetRequiredTask(id);
        var options = project.Options;
        string name = _name.Text.Trim();
        ParsedDuration? duration = null;
        if (!task.IsSummary && _duration.Text != DurationText.Format(task.DurationMinutes, options, isElapsed: task.IsElapsedDuration, isEstimated: task.IsEstimated))
        {
            if (!DurationText.TryParse(_duration.Text, options, out var d))
            {
                _viewModel.ShowError($"'{_duration.Text}' is not a valid duration.");
                return;
            }
            duration = d;
        }
        DateTime? start = null, finish = null;
        if (!task.IsSummary && _start.Text != DateText.Format(task.Start))
        {
            if (!DateText.TryParse(_start.Text, project.Calendar, false, out var s))
            {
                _viewModel.ShowError("Enter a valid start date.");
                return;
            }
            start = s;
        }
        if (!task.IsSummary && _finish.Text != DateText.Format(task.Finish))
        {
            if (!DateText.TryParse(_finish.Text, project.Calendar, true, out var f))
            {
                _viewModel.ShowError("Enter a valid finish date.");
                return;
            }
            finish = f;
        }
        double? percent = null;
        if (!task.IsSummary && _percent.Text != task.PercentComplete.ToString("0.#", CultureInfo.CurrentCulture) + "%")
        {
            if (!double.TryParse(_percent.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var pc) || pc is < 0 or > 100)
            {
                _viewModel.ShowError("% complete must be a number from 0 to 100.");
                return;
            }
            percent = pc;
        }

        _viewModel.TryEdit("Task form", p =>
        {
            var t = p.GetRequiredTask(id);
            if (name.Length > 0)
                t.Name = name;
            if (duration is { } d)
            {
                p.SetDuration(t, d.Minutes);
                t.IsElapsedDuration = d.IsElapsed;
                t.IsEstimated = d.IsEstimated;
            }
            // A typed start or finish sets a constraint, as typing in the Gantt sheet does.
            if (start is { } s && t.Mode == TaskMode.AutoScheduled)
            {
                t.ConstraintType = ConstraintType.StartNoEarlierThan;
                t.ConstraintDate = s;
            }
            else if (start is { } ms)
            {
                t.ManualStart = ms;
            }
            if (finish is { } f && t.Mode == TaskMode.AutoScheduled)
            {
                t.ConstraintType = ConstraintType.FinishNoEarlierThan;
                t.ConstraintDate = f;
            }
            else if (finish is { } mf)
            {
                t.ManualFinish = mf;
            }
            if (percent is { } pc)
                TaskProgress.SetPercentComplete(t, pc);
        });
    }
}
