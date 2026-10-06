using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Calendars;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>
/// Change Working Time: edit the work week and exceptions of base and resource calendars, or create
/// new base calendars. Edits to every calendar touched are applied together as one undoable change.
/// </summary>
public partial class ChangeWorkingTimeDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly ObservableCollection<CalendarModel> _calendars = new();

    public ChangeWorkingTimeDialog(MainViewModel owner, WorkingCalendar? initial = null)
    {
        _owner = owner;
        InitializeComponent();

        var project = owner.Project;
        foreach (var calendar in project.Calendars)
            _calendars.Add(new CalendarModel(calendar, calendar == project.Calendar ? $"{calendar.Name} (Project Calendar)" : calendar.Name));
        foreach (var resource in project.Resources.Where(r => r.Calendar is not null))
            _calendars.Add(new CalendarModel(resource.Calendar!, $"Resource: {resource.Name}"));

        CalendarPicker.ItemsSource = _calendars;
        CalendarPicker.SelectedItem = _calendars.FirstOrDefault(c => c.Original == (initial ?? project.Calendar)) ?? _calendars.FirstOrDefault();
    }

    private CalendarModel? Selected => CalendarPicker.SelectedItem as CalendarModel;

    private void OnCalendarChanged(object sender, SelectionChangedEventArgs e)
    {
        WeekGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ExceptionGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (Selected is not { } model)
            return;
        WeekGrid.ItemsSource = model.Week;
        ExceptionGrid.ItemsSource = model.Exceptions;
        WeekGrid.Columns[1].Visibility = model.Base is null ? Visibility.Collapsed : Visibility.Visible;
        BaseInfo.Text = model.Base is null ? "Base calendar." : $"Based on: {model.Base.Name}";
    }

    private void OnCreateCalendar(object sender, RoutedEventArgs e)
    {
        NewCalendarPanel.Visibility = Visibility.Visible;
        NewCalendarName.Text = $"Calendar {_calendars.Count(c => c.Base is null) + 1}";
        NewCalendarName.Focus();
        NewCalendarName.SelectAll();
    }

    private void OnConfirmCreate(object sender, RoutedEventArgs e)
    {
        var name = NewCalendarName.Text.Trim();
        if (name.Length == 0)
            return;
        if (_calendars.Any(c => c.Base is null && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"A calendar named '{name}' already exists.", "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // New base calendars start from the Standard work week, or copy the selected calendar's effective hours.
        var source = NewCopy.IsChecked == true && Selected is { } selected
            ? selected.Build(selected.Original?.BaseCalendar)
            : WorkingCalendar.CreateStandard(name);
        var model = new CalendarModel(source.Rebase(null, name), name) { IsNew = true };
        _calendars.Add(model);
        CalendarPicker.SelectedItem = model;
        NewCalendarPanel.Visibility = Visibility.Collapsed;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        WeekGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ExceptionGrid.CommitEdit(DataGridEditingUnit.Row, true);

        // Validate everything up front so errors name the calendar.
        try
        {
            foreach (var model in _calendars)
                model.Validate();
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_owner.TryEdit("Change working time", Apply))
            DialogResult = true;
    }

    private void Apply(Project project)
    {
        foreach (var model in _calendars)
        {
            if (model.IsNew)
            {
                var calendar = new WorkingCalendar(model.Name);
                model.ApplyTo(calendar);
                project.Calendars.Add(calendar);
            }
            else if (model.Original is { } original)
            {
                model.ApplyTo(original);
            }
        }
    }

    /// <summary>Editable copy of one calendar.</summary>
    public sealed class CalendarModel
    {
        public CalendarModel(WorkingCalendar calendar, string label)
        {
            Original = calendar;
            Base = calendar.BaseCalendar;
            Name = calendar.Name;
            Label = label;
            var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            for (int i = 0; i < 7; i++)
            {
                var day = (DayOfWeek)(((int)first + i) % 7);
                Week.Add(new WeekDayRow
                {
                    Day = day,
                    DayName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day),
                    UseBase = Base is not null && calendar.IsWorkWeekDayInherited(day),
                    WorkingTimes = calendar.GetWorkWeekDay(day).Count == 0 ? "" : WorkingTimeText.Format(calendar.GetWorkWeekDay(day)),
                });
            }
            foreach (var e in calendar.Exceptions)
            {
                Exceptions.Add(new ExceptionRow
                {
                    Name = e.Name,
                    Start = DateText.FormatDay(e.From),
                    Finish = DateText.FormatDay(e.To),
                    WorkingTimes = e.IsWorking ? WorkingTimeText.Format(e.WorkingTimes) : "",
                    RepeatYearly = e.RepeatYearly,
                });
            }
        }

        public WorkingCalendar? Original { get; }
        public WorkingCalendar? Base { get; }
        public string Name { get; }
        public string Label { get; }
        public bool IsNew { get; init; }
        public ObservableCollection<WeekDayRow> Week { get; } = new();
        public ObservableCollection<ExceptionRow> Exceptions { get; } = new();

        public void Validate() => Build(Base);

        /// <summary>A standalone calendar with this model's settings (used for validation and copies).</summary>
        public WorkingCalendar Build(WorkingCalendar? baseCalendar)
        {
            var calendar = new WorkingCalendar(Name, baseCalendar);
            ApplyTo(calendar);
            return calendar;
        }

        public void ApplyTo(WorkingCalendar calendar)
        {
            foreach (var row in Week)
            {
                if (row.UseBase && calendar.BaseCalendar is not null)
                    calendar.InheritWorkWeekDay(row.Day);
                else
                    calendar.SetWorkWeekDay(row.Day, Parse(row.WorkingTimes, $"{Name}, {row.DayName}").ToArray());
            }

            var exceptions = new List<CalendarException>();
            foreach (var row in Exceptions)
            {
                if (string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.Start))
                    continue;
                if (!DateText.TryParseDay(row.Start, out var from))
                    throw new FormatException($"{Name}: exception '{row.Name}' needs a valid start date.");
                var to = from;
                if (!string.IsNullOrWhiteSpace(row.Finish) && !DateText.TryParseDay(row.Finish, out to))
                    throw new FormatException($"{Name}: exception '{row.Name}' has an invalid finish date.");
                if (to < from)
                    throw new FormatException($"{Name}: exception '{row.Name}' finishes before it starts.");
                exceptions.Add(new CalendarException(row.Name ?? "", from, to, Parse(row.WorkingTimes, $"{Name}, {row.Name}")) { RepeatYearly = row.RepeatYearly });
            }
            calendar.Exceptions.Clear();
            calendar.Exceptions.AddRange(exceptions);
        }

        private static IReadOnlyList<WorkingTime> Parse(string? text, string where)
        {
            try
            {
                return WorkingTimeText.Parse(text);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"{where}: {ex.Message}");
            }
        }
    }

    public sealed class WeekDayRow
    {
        public DayOfWeek Day { get; init; }
        public string DayName { get; init; } = "";
        public bool UseBase { get; set; }
        public string? WorkingTimes { get; set; }
    }

    public sealed class ExceptionRow
    {
        public string? Name { get; set; }
        public string? Start { get; set; }
        public string? Finish { get; set; }
        public string? WorkingTimes { get; set; }
        public bool RepeatYearly { get; set; }
    }
}
