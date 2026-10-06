using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.App.Dialogs;

/// <summary>Row layout helpers for the tracking dialogs (window setup and buttons come from <see cref="Form"/>).</summary>
internal static class Rows
{
    public static TextBox Box(double width = 140) => new() { Width = width, Padding = new Thickness(3, 2, 3, 2), Margin = new Thickness(0, 2, 12, 2), VerticalAlignment = VerticalAlignment.Center };

    public static TextBlock Label(string text, double width = 0)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        if (width > 0)
            label.Width = width;
        return label;
    }

    public static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }
}

/// <summary>
/// Update Tasks: % complete, actual and remaining duration, actual start and finish for the selected
/// tasks. With several tasks selected, only the values typed in are applied to each.
/// </summary>
public sealed class UpdateTasksDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly List<int> _ids;
    private readonly TextBox _percent = Rows.Box(70), _actualDuration = Rows.Box(90), _remainingDuration = Rows.Box(90), _actualStart = Rows.Box(), _actualFinish = Rows.Box();
    private readonly string _initialPercent, _initialActual, _initialRemaining, _initialStart, _initialFinish;

    public UpdateTasksDialog(MainViewModel owner)
    {
        _owner = owner;
        _ids = owner.SelectedLeafIds();
        Form.Setup(this, "Update Tasks");
        var options = owner.Project.Options;
        var tasks = _ids.Select(owner.Project.GetRequiredTask).ToList();

        string name = tasks.Count == 1 ? tasks[0].Name : $"{tasks.Count} tasks";
        string current = "";
        if (tasks.Count == 1)
        {
            var t = tasks[0];
            _percent.Text = t.PercentComplete.ToString("0.##", CultureInfo.CurrentCulture) + "%";
            _actualDuration.Text = DurationText.Format(t.ActualDurationMinutes, options);
            _remainingDuration.Text = DurationText.Format(t.RemainingDurationMinutes, options);
            _actualStart.Text = DateText.Format(t.ActualStart);
            _actualFinish.Text = DateText.Format(t.ActualFinish);
            current = $"Current: {DateText.Format(t.Start)} to {DateText.Format(t.Finish)}, duration {DurationText.Format(t.DurationMinutes, options)}";
        }
        (_initialPercent, _initialActual, _initialRemaining, _initialStart, _initialFinish) =
            (_percent.Text, _actualDuration.Text, _remainingDuration.Text, _actualStart.Text, _actualFinish.Text);

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 470,
            Children =
            {
                Rows.Row(Rows.Label("Name:", 110), new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }),
                new TextBlock { Text = current, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 2, 0, 6) },
                Rows.Row(Rows.Label("% Complete:", 110), _percent),
                Rows.Row(Rows.Label("Actual dur:", 110), _actualDuration, Rows.Label("Remaining dur:"), _remainingDuration),
                Rows.Heading("Actual"),
                Rows.Row(Rows.Label("Start:", 110), _actualStart),
                Rows.Row(Rows.Label("Finish:", 110), _actualFinish),
                new TextBlock
                {
                    Text = "Leave a box unchanged to keep its value. An actual finish marks the task complete.",
                    TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 8, 0, 0),
                },
                Form.Buttons(this, Apply),
            },
        };
    }

    private void Apply()
    {
        var project = _owner.Project;
        var options = project.Options;
        double? percent = null;
        long? actual = null, remaining = null;
        DateTime? start = null, finish = null;

        if (_percent.Text != _initialPercent && _percent.Text.Trim().Length > 0)
        {
            if (!double.TryParse(_percent.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var p) || p is < 0 or > 100)
            {
                Form.Warn(this, "% complete must be a number from 0 to 100.");
                return;
            }
            percent = p;
        }
        if (_actualDuration.Text != _initialActual && _actualDuration.Text.Trim().Length > 0)
        {
            if (!DurationText.TryParse(_actualDuration.Text, options, out var d))
            {
                Form.Warn(this, $"'{_actualDuration.Text}' is not a valid duration.");
                return;
            }
            actual = d.Minutes;
        }
        if (_remainingDuration.Text != _initialRemaining && _remainingDuration.Text.Trim().Length > 0)
        {
            if (!DurationText.TryParse(_remainingDuration.Text, options, out var d))
            {
                Form.Warn(this, $"'{_remainingDuration.Text}' is not a valid duration.");
                return;
            }
            remaining = d.Minutes;
        }
        if (_actualStart.Text != _initialStart && _actualStart.Text.Trim().Length > 0)
        {
            if (!DateText.TryParse(_actualStart.Text, project.Calendar, false, out var s))
            {
                Form.Warn(this, "Enter a valid actual start date.");
                return;
            }
            start = s;
        }
        if (_actualFinish.Text != _initialFinish && _actualFinish.Text.Trim().Length > 0)
        {
            if (!DateText.TryParse(_actualFinish.Text, project.Calendar, true, out var f))
            {
                Form.Warn(this, "Enter a valid actual finish date.");
                return;
            }
            finish = f;
        }
        // An actual duration typed with the remaining one unchanged keeps the remaining duration.
        if (actual is not null && remaining is null && _ids.Count == 1)
            remaining = project.GetRequiredTask(_ids[0]).RemainingDurationMinutes;

        if (percent is null && actual is null && remaining is null && start is null && finish is null)
        {
            DialogResult = false;
            return;
        }
        if (_owner.TryEdit("Update tasks", p =>
            {
                foreach (var id in _ids)
                    TaskProgress.UpdateTask(p, p.GetRequiredTask(id), percent, actual, remaining, start, finish);
            }))
            DialogResult = true;
    }
}

/// <summary>
/// Update Project: mark work complete through a date, or reschedule unfinished work to start after it,
/// for the entire project or the selected tasks.
/// </summary>
public sealed class UpdateProjectDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly RadioButton _complete = new() { Content = "Update work as complete through:", IsChecked = true, GroupName = "action" };
    private readonly RadioButton _reschedule = new() { Content = "Reschedule uncompleted work to start after:", GroupName = "action" };
    private readonly RadioButton _proportional = new() { Content = "Set 0% - 100% complete", IsChecked = true, GroupName = "mode", Margin = new Thickness(24, 2, 0, 2) };
    private readonly RadioButton _zeroOrHundred = new() { Content = "Set 0% or 100% complete only", GroupName = "mode", Margin = new Thickness(24, 2, 0, 2) };
    private readonly RadioButton _entire = new() { Content = "Entire project", IsChecked = true, GroupName = "scope" };
    private readonly RadioButton _selected = new() { Content = "Selected tasks", GroupName = "scope", Margin = new Thickness(16, 0, 0, 0) };
    private readonly TextBox _completeDate = Rows.Box(), _rescheduleDate = Rows.Box();

    public UpdateProjectDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Update Project");
        var date = DateText.Format(owner.Project.StatusDate ?? DateTime.Today.AddHours(17));
        _completeDate.Text = date;
        _rescheduleDate.Text = date;
        _selected.IsEnabled = owner.SelectedTasks.Count > 0;
        _complete.Width = _reschedule.Width = 290;

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 460,
            Children =
            {
                Rows.Row(_complete, _completeDate),
                _proportional,
                _zeroOrHundred,
                Rows.Row(_reschedule, _rescheduleDate),
                Rows.Heading("For"),
                Rows.Row(_entire, _selected),
                Form.Buttons(this, Apply),
            },
        };
    }

    private void Apply()
    {
        var project = _owner.Project;
        var box = _complete.IsChecked == true ? _completeDate : _rescheduleDate;
        if (!DateText.TryParse(box.Text, project.Calendar, _complete.IsChecked == true, out var date))
        {
            Form.Warn(this, "Enter a valid date.");
            return;
        }
        var ids = _selected.IsChecked == true ? _owner.SelectedTasks.Select(t => t.Id).ToList() : null;
        int count = 0;
        bool ok = _owner.TryEdit("Update project", p =>
        {
            var tasks = ids?.Select(p.GetRequiredTask).ToList();
            count = _complete.IsChecked == true
                ? TaskProgress.UpdateCompleteThrough(p, date, _proportional.IsChecked == true ? UpdateProgressMode.Proportional : UpdateProgressMode.ZeroOrHundred, tasks).Count
                : TaskProgress.RescheduleUncompletedWork(p, date, tasks).Count;
        });
        if (!ok)
            return;
        MessageBox.Show(this, $"{count} task(s) updated.", "Update Project", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }
}
