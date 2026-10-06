using System.Globalization;
using System.Windows;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>Project Information: dates, scheduling direction, status date, calendar, options and statistics.</summary>
public partial class ProjectInformationDialog : Window
{
    private readonly MainViewModel _owner;

    public ProjectInformationDialog(MainViewModel owner)
    {
        _owner = owner;
        var p = owner.Project;
        var o = p.Options;

        ProjectName = p.Name;
        ScheduleFromIndex = p.ScheduleFromFinish ? 1 : 0;
        StartDate = DateText.Format(p.StartDate);
        FinishDate = DateText.Format(p.ScheduleFromFinish ? p.FinishDate : p.Finish);
        StatusDate = DateText.Format(p.StatusDate);
        CalendarNames = p.Calendars.Select(c => c.Name).ToArray();
        CalendarIndex = Math.Max(0, p.Calendars.IndexOf(p.Calendar));
        HoursPerDay = (o.MinutesPerDay / 60.0).ToString("0.##", CultureInfo.CurrentCulture);
        HoursPerWeek = (o.MinutesPerWeek / 60.0).ToString("0.##", CultureInfo.CurrentCulture);
        DaysPerMonth = o.DaysPerMonth.ToString(CultureInfo.CurrentCulture);
        CriticalSlack = DurationText.Format(o.CriticalSlackLimitMinutes, o);
        MultipleCriticalPaths = o.CalculateMultipleCriticalPaths;
        RescheduleToStatusDate = o.RescheduleUncompletedWorkToStatusDate;
        Statistics = BuildStatistics(p);

        InitializeComponent();
        DataContext = this;
    }

    public string ProjectName { get; set; }
    public int ScheduleFromIndex { get; set; }
    public string StartDate { get; set; }
    public string FinishDate { get; set; }
    public string StatusDate { get; set; }
    public IReadOnlyList<string> CalendarNames { get; }
    public int CalendarIndex { get; set; }
    public string HoursPerDay { get; set; }
    public string HoursPerWeek { get; set; }
    public string DaysPerMonth { get; set; }
    public string CriticalSlack { get; set; }
    public bool MultipleCriticalPaths { get; set; }
    public bool RescheduleToStatusDate { get; set; }
    public IReadOnlyList<StatisticRow> Statistics { get; }

    private static IReadOnlyList<StatisticRow> BuildStatistics(Project p)
    {
        var o = p.Options;
        string Dur(long minutes) => DurationText.Format(minutes, o);
        string Work(long minutes) => DurationText.Format(minutes, o, DurationUnit.Hours);
        string Money(decimal value) => value.ToString("C", CultureInfo.CurrentCulture);

        var rows = new List<StatisticRow>
        {
            new("Current", DateText.Format(p.Start), DateText.Format(p.Finish), Dur(p.Calendar.WorkingMinutesBetween(p.Start, p.Finish)), Work(p.WorkMinutes), Money(p.Cost)),
        };
        if (p.Baselines[0] is { } b)
            rows.Add(new("Baseline", DateText.Format(b.Start), DateText.Format(b.Finish), Dur(b.DurationMinutes), Work(b.WorkMinutes), Money(b.Cost)));
        else
            rows.Add(new("Baseline", "NA", "NA", "", "", ""));

        var actualStart = p.Tasks.Where(t => t.ActualStart is not null).Select(t => t.ActualStart!.Value).DefaultIfEmpty().Min();
        var actualWork = p.Tasks.Where(t => !t.IsSummary).Sum(t => t.ActualWorkMinutes);
        rows.Add(new("Actual", actualStart == default ? "NA" : DateText.Format(actualStart), "NA", "", Work(actualWork), Money(p.ActualCost)));
        rows.Add(new("Remaining", "", "", "", Work(p.WorkMinutes - actualWork), Money(p.Cost - p.ActualCost)));
        return rows;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_owner.TryEdit("Project information", Apply))
            DialogResult = true;
    }

    private void Apply(Project p)
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
            throw new FormatException("The project needs a name.");
        p.Name = ProjectName.Trim();
        p.Calendar = p.Calendars[Math.Clamp(CalendarIndex, 0, p.Calendars.Count - 1)];
        p.ScheduleFromFinish = ScheduleFromIndex == 1;

        p.StartDate = DateText.TryParse(StartDate, p.Calendar, false, out var start) ? start
            : throw new FormatException($"'{StartDate}' is not a valid start date.");
        p.FinishDate = DateText.TryParse(FinishDate, p.Calendar, true, out var finish) ? finish
            : throw new FormatException($"'{FinishDate}' is not a valid finish date.");
        if (string.IsNullOrWhiteSpace(StatusDate) || StatusDate.Trim().Equals("NA", StringComparison.OrdinalIgnoreCase))
            p.StatusDate = null;
        else
            p.StatusDate = DateText.TryParse(StatusDate, p.Calendar, true, out var status) ? status
                : throw new FormatException($"'{StatusDate}' is not a valid status date.");

        var o = p.Options;
        o.MinutesPerDay = Minutes(HoursPerDay, "Hours per day", 1, 24);
        o.MinutesPerWeek = Minutes(HoursPerWeek, "Hours per week", 1, 168);
        if (!int.TryParse(DaysPerMonth, NumberStyles.Integer, CultureInfo.CurrentCulture, out var days) || days is < 1 or > 31)
            throw new FormatException("Days per month must be from 1 to 31.");
        o.DaysPerMonth = days;
        o.CriticalSlackLimitMinutes = DurationText.TryParse(CriticalSlack, o, out var slack) ? slack.Minutes
            : throw new FormatException($"'{CriticalSlack}' is not a valid duration.");
        o.CalculateMultipleCriticalPaths = MultipleCriticalPaths;
        o.RescheduleUncompletedWorkToStatusDate = RescheduleToStatusDate;
    }

    private static int Minutes(string text, string what, double min, double max) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var hours) && hours >= min && hours <= max
            ? (int)Math.Round(hours * 60)
            : throw new FormatException($"{what} must be from {min} to {max}.");
}

public sealed record StatisticRow(string Label, string Start, string Finish, string Duration, string Work, string Cost);
