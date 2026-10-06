using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Calendars;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>Resource Information: general details, cost rate tables A-E, and notes.</summary>
public partial class ResourceInformationDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly int _resourceId;
    private readonly ObservableCollection<RateEntry>[] _rateTables = new ObservableCollection<RateEntry>[Resource.RateTableCount];

    public ResourceInformationDialog(MainViewModel owner, Resource resource)
    {
        _owner = owner;
        _resourceId = resource.Id;
        var project = owner.Project;

        ResourceName = resource.Name;
        Initials = resource.Initials;
        Email = resource.Email;
        Group = resource.Group;
        Code = resource.Code;
        TypeIndex = (int)resource.Type;
        MaterialLabel = resource.MaterialLabel;
        MaxUnits = $"{resource.MaxUnits * 100:0.##}%";
        CalendarNames = project.Calendars.Select(c => c.Name).ToArray();
        CalendarIndex = resource.Calendar?.BaseCalendar is { } baseCalendar ? project.Calendars.IndexOf(baseCalendar) : 0;
        AccrualIndex = (int)resource.AccrueAt;
        IsBudget = resource.IsBudget;
        IsGeneric = resource.IsGeneric;
        CanLevel = resource.CanLevel;
        BookingTypeIndex = (int)resource.BookingType;
        Notes = resource.Notes;
        foreach (var period in resource.Availability)
        {
            AvailabilityRows.Add(new AvailabilityRow
            {
                From = DateText.FormatDay(DateOnly.FromDateTime(period.From)),
                To = period.To is { } to ? DateText.FormatDay(DateOnly.FromDateTime(to.AddMinutes(-1))) : "",
                Units = $"{period.Units * 100:0.##}%",
            });
        }

        InitializeComponent();
        DataContext = this;
        Title = $"Resource Information - {resource.Name}";

        for (int i = 0; i < Resource.RateTableCount; i++)
        {
            _rateTables[i] = new ObservableCollection<RateEntry>(resource.CostRateTables[i].Select(r => new RateEntry
            {
                EffectiveDate = r.EffectiveFrom == DateTime.MinValue ? "--" : DateText.FormatDay(DateOnly.FromDateTime(r.EffectiveFrom)),
                StandardRate = r.StandardRate.ToString("0.####", CultureInfo.CurrentCulture),
                OvertimeRate = r.OvertimeRate.ToString("0.####", CultureInfo.CurrentCulture),
                CostPerUse = r.CostPerUse.ToString("0.##", CultureInfo.CurrentCulture),
            }));
            RateTabs.Items.Add(new TabItem { Header = ((char)('A' + i)).ToString(), Content = CreateRateGrid(_rateTables[i]) });
        }
        RateTabs.SelectedIndex = 0;
    }

    public string ResourceName { get; set; }
    public string Initials { get; set; }
    public string Email { get; set; }
    public string Group { get; set; }
    public string Code { get; set; }
    public IReadOnlyList<string> Types => ResourceRowViewModel.Types;
    public int TypeIndex { get; set; }
    public string MaterialLabel { get; set; }
    public string MaxUnits { get; set; }
    public IReadOnlyList<string> CalendarNames { get; }
    public int CalendarIndex { get; set; }
    public IReadOnlyList<string> Accruals => ResourceRowViewModel.Accruals;
    public int AccrualIndex { get; set; }
    public bool IsBudget { get; set; }
    public bool IsGeneric { get; set; }
    public bool CanLevel { get; set; }
    public int BookingTypeIndex { get; set; }
    public string Notes { get; set; }
    public ObservableCollection<AvailabilityRow> AvailabilityRows { get; } = new();

    private static DataGrid CreateRateGrid(ObservableCollection<RateEntry> rows)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 24,
            Margin = new Thickness(0, 4, 0, 0),
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Effective Date", Binding = new System.Windows.Data.Binding(nameof(RateEntry.EffectiveDate)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Standard Rate", Binding = new System.Windows.Data.Binding(nameof(RateEntry.StandardRate)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Overtime Rate", Binding = new System.Windows.Data.Binding(nameof(RateEntry.OvertimeRate)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Per Use Cost", Binding = new System.Windows.Data.Binding(nameof(RateEntry.CostPerUse)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        return grid;
    }

    private void OnChangeWorkingTime(object sender, RoutedEventArgs e)
    {
        var resource = _owner.Project.GetResource(_resourceId);
        if (resource?.Calendar is null)
        {
            MessageBox.Show(this, "Only work resources have a working calendar.", "Planora", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new ChangeWorkingTimeDialog(_owner, resource.Calendar) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        VisualTreeSearch.FindChild<DataGrid>(this)?.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (TabItem tab in RateTabs.Items)
            (tab.Content as DataGrid)?.CommitEdit(DataGridEditingUnit.Row, true);
        if (_owner.TryEdit("Resource information", Apply))
            DialogResult = true;
    }

    private void Apply(Project p)
    {
        var r = p.GetResource(_resourceId) ?? throw new InvalidOperationException("The resource no longer exists.");
        if (string.IsNullOrWhiteSpace(ResourceName))
            throw new FormatException("The resource needs a name.");

        var newType = (ResourceType)TypeIndex;
        if (newType != r.Type && p.AssignmentsOf(r).Any())
            throw new InvalidOperationException("Remove this resource's assignments before changing its type.");
        if (IsBudget && !r.IsBudget && p.AssignmentsOf(r).Any())
            throw new InvalidOperationException("A budget resource cannot have task assignments.");

        r.Name = ResourceName.Trim();
        r.Initials = Initials;
        r.Email = Email;
        r.Group = Group;
        r.Code = Code;
        r.Type = newType;
        r.MaterialLabel = MaterialLabel;
        r.AccrueAt = (CostAccrual)AccrualIndex;
        r.IsBudget = IsBudget;
        r.IsGeneric = IsGeneric;
        r.CanLevel = CanLevel;
        r.BookingType = (BookingType)BookingTypeIndex;
        r.Notes = Notes;

        if (!double.TryParse(MaxUnits.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var maxUnits) || maxUnits < 0)
            throw new FormatException("Max units must be a percentage, e.g. 100%.");
        r.MaxUnits = maxUnits / 100;

        if (r.Type == ResourceType.Work)
        {
            var newBase = p.Calendars[Math.Clamp(CalendarIndex, 0, p.Calendars.Count - 1)];
            r.Calendar = r.Calendar is null ? new WorkingCalendar(r.Name, newBase)
                : r.Calendar.BaseCalendar == newBase ? r.Calendar
                : r.Calendar.Rebase(newBase, r.Name);
        }
        else
        {
            r.Calendar = null;
        }

        var periods = new List<AvailabilityPeriod>();
        foreach (var row in AvailabilityRows.Where(a => !string.IsNullOrWhiteSpace(a.From)))
        {
            if (!DateText.TryParseDay(row.From, out var from))
                throw new FormatException($"'{row.From}' is not a valid 'available from' date.");
            DateTime? to = null;
            if (!string.IsNullOrWhiteSpace(row.To))
            {
                if (!DateText.TryParseDay(row.To, out var toDay) || toDay < from)
                    throw new FormatException($"'{row.To}' is not a valid 'available to' date on or after the start.");
                to = toDay.ToDateTime(TimeOnly.MinValue).AddDays(1); // through the end of that day
            }
            if (!double.TryParse((row.Units ?? "").Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var units) || units < 0)
                throw new FormatException($"'{row.Units}' is not a valid number of units (e.g. 50%).");
            periods.Add(new AvailabilityPeriod(from.ToDateTime(TimeOnly.MinValue), to, units / 100));
        }
        periods.Sort((a, b) => a.From.CompareTo(b.From));
        for (int i = 1; i < periods.Count; i++)
        {
            if (periods[i - 1].To is null || periods[i - 1].To > periods[i].From)
                throw new FormatException("Availability periods must not overlap.");
        }
        r.Availability.Clear();
        r.Availability.AddRange(periods);
        for (int i = 0; i < Resource.RateTableCount; i++)
        {
            var rates = new List<CostRate>();
            foreach (var row in _rateTables[i])
            {
                if (string.IsNullOrWhiteSpace(row.StandardRate) && string.IsNullOrWhiteSpace(row.EffectiveDate))
                    continue;
                DateTime effective = DateTime.MinValue;
                if (rates.Count > 0)
                {
                    if (!DateText.TryParseDay(row.EffectiveDate, out var day))
                        throw new FormatException($"Rate table {(char)('A' + i)}: '{row.EffectiveDate}' is not a valid effective date.");
                    effective = day.ToDateTime(TimeOnly.MinValue);
                }
                rates.Add(new CostRate(effective, Money(row.StandardRate, i), Money(row.OvertimeRate, i), Money(row.CostPerUse, i)));
            }
            if (rates.Count == 0)
                rates.Add(new CostRate(DateTime.MinValue, 0));
            if (rates.Select(x => x.EffectiveFrom).Distinct().Count() != rates.Count)
                throw new FormatException($"Rate table {(char)('A' + i)} has two rows with the same date.");
            r.CostRateTables[i].Clear();
            r.CostRateTables[i].AddRange(rates.OrderBy(x => x.EffectiveFrom));
        }
    }

    private static decimal Money(string? text, int table)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return ResourceRowViewModel.TryParseMoney(text, out var amount) && amount >= 0
            ? amount
            : throw new FormatException($"Rate table {(char)('A' + table)}: '{text}' is not a valid amount.");
    }
}

/// <summary>A row of the resource availability table.</summary>
public sealed class AvailabilityRow
{
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Units { get; set; } = "100%";
}

/// <summary>A row of a cost rate table.</summary>
public sealed class RateEntry
{
    public string? EffectiveDate { get; set; }
    public string? StandardRate { get; set; }
    public string? OvertimeRate { get; set; }
    public string? CostPerUse { get; set; }
}
