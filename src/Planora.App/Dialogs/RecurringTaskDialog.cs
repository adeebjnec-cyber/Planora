using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>Recurring Task Information: create or edit a task that repeats on a pattern.</summary>
public sealed class RecurringTaskDialog : Window
{
    private static readonly string[] Ordinals = { "First", "Second", "Third", "Fourth", "Last" };

    private readonly MainViewModel _owner;
    private readonly int? _editingId;
    private readonly int _insertIndex;
    private readonly TextBox _name = Box(260), _duration = Box(90), _interval = Box(50), _dayOfMonth = Box(50), _start = Box(140), _end = Box(140), _count = Box(60);
    private readonly RadioButton _daily = new() { Content = "Daily" }, _weekly = new() { Content = "Weekly" }, _monthly = new() { Content = "Monthly" }, _yearly = new() { Content = "Yearly" };
    private readonly RadioButton _byDay = new() { Content = "Day", IsChecked = true }, _byWeekday = new() { Content = "The" };
    private readonly RadioButton _endAfter = new() { Content = "End after:", IsChecked = true }, _endBy = new() { Content = "End by:" };
    private readonly ComboBox _ordinal = new() { ItemsSource = Ordinals, SelectedIndex = 0, Width = 80 };
    private readonly ComboBox _weekday = new() { Width = 110 };
    private readonly ComboBox _month = new() { Width = 110 };
    private readonly CheckBox[] _days = new CheckBox[7];
    private readonly CheckBox _skipNonWorking = new() { Content = "Skip occurrences on non-working days", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _weeklyPanel = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _monthlyPanel = new();
    private readonly TextBlock _unit = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

    public RecurringTaskDialog(MainViewModel owner, ProjectTask? editing = null)
    {
        _owner = owner;
        _editingId = editing?.Id;
        _insertIndex = owner.SelectedTasks.Count > 0 ? owner.Project.IndexOf(owner.SelectedTasks[0]) : owner.Project.Tasks.Count;
        Title = "Recurring Task Information";
        Owner = Application.Current.MainWindow;
        Style = (Style)Application.Current.Resources["DialogWindow"];
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var culture = CultureInfo.CurrentCulture.DateTimeFormat;
        _weekday.ItemsSource = Enum.GetValues<DayOfWeek>().Select(d => culture.GetDayName(d)).ToList();
        _month.ItemsSource = Enumerable.Range(1, 12).Select(m => culture.GetMonthName(m)).ToList();
        for (int d = 0; d < 7; d++)
        {
            var day = (DayOfWeek)(((int)culture.FirstDayOfWeek + d) % 7);
            _days[(int)day] = new CheckBox { Content = culture.GetAbbreviatedDayName(day), Margin = new Thickness(0, 0, 10, 0) };
            _weeklyPanel.Children.Add(_days[(int)day]);
        }
        foreach (var radio in new[] { _daily, _weekly, _monthly, _yearly })
        {
            radio.GroupName = "frequency";
            radio.Margin = new Thickness(0, 2, 0, 2);
            radio.Checked += (_, _) => UpdatePanels();
        }
        _byDay.GroupName = _byWeekday.GroupName = "monthly";
        _endAfter.GroupName = _endBy.GroupName = "range";

        // Defaults, or the existing pattern.
        var pattern = editing?.Recurrence ?? new RecurrencePattern
        {
            Frequency = RecurrenceFrequency.Weekly,
            DaysOfWeek = { (_owner.Project.Tasks.Count > 0 ? _owner.Project.Start : _owner.Project.StartDate).DayOfWeek },
            Start = DateOnly.FromDateTime(_owner.Project.Tasks.Count > 0 ? _owner.Project.Start : _owner.Project.StartDate),
            Occurrences = 10,
            OccurrenceDurationMinutes = 60,
        };
        _name.Text = editing?.Name ?? "Status meeting";
        _duration.Text = DurationText.Format(pattern.OccurrenceDurationMinutes, owner.Project.Options, pattern.OccurrenceDurationMinutes < owner.Project.Options.MinutesPerDay ? DurationUnit.Hours : DurationUnit.Days);
        _interval.Text = pattern.Interval.ToString(CultureInfo.CurrentCulture);
        (pattern.Frequency switch { RecurrenceFrequency.Daily => _daily, RecurrenceFrequency.Monthly => _monthly, RecurrenceFrequency.Yearly => _yearly, _ => _weekly }).IsChecked = true;
        foreach (var day in pattern.DaysOfWeek)
            _days[(int)day].IsChecked = true;
        _dayOfMonth.Text = (pattern.DayOfMonth ?? pattern.Start.Day).ToString(CultureInfo.CurrentCulture);
        if (pattern.WeekOfMonth is { } week)
        {
            _byWeekday.IsChecked = true;
            _ordinal.SelectedIndex = Math.Clamp(week, 1, 5) - 1;
        }
        _weekday.SelectedIndex = (int)pattern.DayOfWeek;
        _month.SelectedIndex = Math.Clamp(pattern.Month, 1, 12) - 1;
        _start.Text = DateText.FormatDay(pattern.Start);
        _count.Text = (pattern.Occurrences ?? 10).ToString(CultureInfo.CurrentCulture);
        _end.Text = pattern.End is { } end ? DateText.FormatDay(end) : DateText.FormatDay(pattern.Start.AddMonths(3));
        if (pattern.End is not null && pattern.Occurrences is null)
            _endBy.IsChecked = true;
        _skipNonWorking.IsChecked = pattern.SkipNonWorkingDays;

        _monthlyPanel.Children.Add(Row(_byDay, _dayOfMonth));
        _monthlyPanel.Children.Add(Row(_byWeekday, _ordinal, _weekday));

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        ok.Click += (_, _) => Apply();

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 520,
            Children =
            {
                Row(Label("Task name:"), _name, Label("Duration:"), _duration),
                Heading("Recurrence pattern"),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new StackPanel { Width = 100, Children = { _daily, _weekly, _monthly, _yearly } },
                        new StackPanel
                        {
                            Children =
                            {
                                Row(Label("Every"), _interval, _unit),
                                _weeklyPanel,
                                Row(Label("Month:"), _month),
                                _monthlyPanel,
                            },
                        },
                    },
                },
                Heading("Range of recurrence"),
                Row(Label("Start:"), _start),
                Row(_endAfter, _count, Label("occurrences")),
                Row(_endBy, _end),
                _skipNonWorking,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } },
            },
        };
        UpdatePanels();
    }

    private void UpdatePanels()
    {
        _unit.Text = _daily.IsChecked == true ? "day(s)" : _weekly.IsChecked == true ? "week(s)" : _monthly.IsChecked == true ? "month(s)" : "year(s)";
        _weeklyPanel.Visibility = _weekly.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _monthlyPanel.Visibility = _monthly.IsChecked == true || _yearly.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        // The month row may not exist yet while the dialog is being built.
        if (_month.Parent is FrameworkElement monthRow)
            monthRow.Visibility = _yearly.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Apply()
    {
        var options = _owner.Project.Options;
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            Warn("Enter a task name.");
            return;
        }
        if (!DurationText.TryParse(_duration.Text, options, out var duration, DurationUnit.Hours))
        {
            Warn($"'{_duration.Text}' is not a valid duration.");
            return;
        }
        if (!int.TryParse(_interval.Text, out var interval) || interval < 1)
        {
            Warn("Enter how often the task repeats (1 or more).");
            return;
        }
        if (!DateText.TryParseDay(_start.Text, out var start))
        {
            Warn("Enter a valid start date.");
            return;
        }

        var pattern = new RecurrencePattern
        {
            Frequency = _daily.IsChecked == true ? RecurrenceFrequency.Daily : _weekly.IsChecked == true ? RecurrenceFrequency.Weekly
                : _monthly.IsChecked == true ? RecurrenceFrequency.Monthly : RecurrenceFrequency.Yearly,
            Interval = interval,
            OccurrenceDurationMinutes = duration.Minutes,
            Start = start,
            SkipNonWorkingDays = _skipNonWorking.IsChecked == true,
            DayOfWeek = (DayOfWeek)Math.Max(0, _weekday.SelectedIndex),
            Month = _month.SelectedIndex + 1,
        };
        pattern.DaysOfWeek.AddRange(Enumerable.Range(0, 7).Where(d => _days[d].IsChecked == true).Select(d => (DayOfWeek)d));
        if (pattern.Frequency == RecurrenceFrequency.Weekly && pattern.DaysOfWeek.Count == 0)
        {
            Warn("Choose at least one day of the week.");
            return;
        }
        if (_byWeekday.IsChecked == true)
            pattern.WeekOfMonth = _ordinal.SelectedIndex + 1;
        else if (int.TryParse(_dayOfMonth.Text, out var day) && day is >= 1 and <= 31)
            pattern.DayOfMonth = day;
        else if (pattern.Frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly)
        {
            Warn("Enter a day of the month from 1 to 31.");
            return;
        }

        if (_endAfter.IsChecked == true)
        {
            if (!int.TryParse(_count.Text, out var count) || count < 1 || count > RecurrencePattern.MaxOccurrences)
            {
                Warn($"Enter 1 to {RecurrencePattern.MaxOccurrences} occurrences.");
                return;
            }
            pattern.Occurrences = count;
        }
        else if (DateText.TryParseDay(_end.Text, out var end) && end >= start)
            pattern.End = end;
        else
        {
            Warn("Enter an end date on or after the start date.");
            return;
        }

        var name = _name.Text.Trim();
        bool ok = _owner.TryEdit(_editingId is null ? "Insert recurring task" : "Edit recurring task", p =>
        {
            if (_editingId is { } id)
            {
                var summary = p.GetRequiredTask(id);
                summary.Name = name;
                p.SetRecurrence(summary, pattern);
            }
            else
            {
                p.AddRecurringTask(name, pattern, _insertIndex);
            }
        });
        if (ok)
            DialogResult = true;
    }

    private void Warn(string message) => MessageBox.Show(this, message, "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static TextBox Box(double width) => new() { Width = width, Padding = new Thickness(3, 2, 3, 2), Margin = new Thickness(0, 2, 8, 2) };

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var child in children)
        {
            if (child is FrameworkElement { VerticalAlignment: not VerticalAlignment.Center } element)
                element.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(child);
        }
        return row;
    }
}
