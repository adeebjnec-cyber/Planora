using System.Globalization;
using Planora.App.Infrastructure;
using Planora.Engine.Calendars;
using Planora.Engine.Model;

namespace Planora.App.ViewModels;

/// <summary>One row of the Resource Sheet. The last row is a placeholder for typing a new resource.</summary>
public sealed class ResourceRowViewModel : ObservableObject
{
    public static readonly string[] Types = { "Work", "Material", "Cost" };
    public static readonly string[] Accruals = { "Start", "Prorated", "End" };

    private readonly MainViewModel _owner;

    public ResourceRowViewModel(MainViewModel owner, Resource? resource)
    {
        _owner = owner;
        Resource = resource;
    }

    public Resource? Resource { get; private set; }
    public bool IsPlaceholder => Resource is null;
    private Project Project => _owner.Project;

    internal void Rebind(Resource? resource)
    {
        Resource = resource;
        OnAllPropertiesChanged();
    }

    internal void Refresh() => OnAllPropertiesChanged();

    public string RowNumber => Resource is null ? "" : (Project.Resources.ToList().IndexOf(Resource) + 1).ToString(CultureInfo.CurrentCulture);
    public bool IsOverallocated => Resource?.IsOverallocated ?? false;
    public string Indicators => Resource is null ? "" : (Resource.IsOverallocated ? "⚠" : "") + (string.IsNullOrWhiteSpace(Resource.Notes) ? "" : " ✎");
    public bool IsWork => Resource?.Type == ResourceType.Work;
    public string Work => Resource is null ? "" : DurationText.Format(Resource.WorkMinutes, Project.Options, DurationUnit.Hours);
    public string Cost => Resource is null ? "" : Resource.Cost.ToString("C", CultureInfo.CurrentCulture);
    public string PeakUnits => Resource is { Type: ResourceType.Work } r ? $"{r.PeakUnits * 100:0.#}%" : "";

    public string Name
    {
        get => Resource?.Name ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (Resource is null)
            {
                _owner.AddResourceFromPlaceholder(value.Trim());
                return;
            }
            if (value != Resource.Name)
                Edit("Rename resource", (_, r) => r.Name = value.Trim());
        }
    }

    public string Type
    {
        get => Resource is null ? "" : Types[(int)Resource.Type];
        set
        {
            int index = Array.IndexOf(Types, value);
            if (Resource is null || index < 0 || index == (int)Resource.Type)
                return;
            Edit("Change resource type", (p, r) =>
            {
                if (p.Assignments.Any(a => a.Resource == r))
                    throw new InvalidOperationException("Remove this resource's assignments before changing its type.");
                r.Type = (ResourceType)index;
                r.Calendar = r.Type == ResourceType.Work ? r.Calendar ?? new WorkingCalendar(r.Name, p.Calendar) : null;
            });
        }
    }

    public string MaterialLabel
    {
        get => Resource?.MaterialLabel ?? "";
        set => SetText(value, Resource?.MaterialLabel, "Change material label", r => r.MaterialLabel = value);
    }

    public string Initials
    {
        get => Resource?.Initials ?? "";
        set => SetText(value, Resource?.Initials, "Change initials", r => r.Initials = value);
    }

    public string Group
    {
        get => Resource?.Group ?? "";
        set => SetText(value, Resource?.Group, "Change group", r => r.Group = value);
    }

    public string Code
    {
        get => Resource?.Code ?? "";
        set => SetText(value, Resource?.Code, "Change code", r => r.Code = value);
    }

    public string MaxUnits
    {
        get => Resource is { Type: ResourceType.Work } r ? $"{r.MaxUnits * 100:0.##}%" : "";
        set
        {
            if (Resource is not { Type: ResourceType.Work } || value == MaxUnits)
                return;
            if (!TryParseNumber(value.TrimEnd('%'), out var percent) || percent < 0)
            {
                _owner.ShowError("Max units must be a percentage, e.g. 100% or 50%.");
                OnPropertyChanged();
                return;
            }
            Edit("Change max units", (_, r) => r.MaxUnits = percent / 100);
        }
    }

    public string StandardRate
    {
        get => Resource is null ? "" : FormatRate(Resource.StandardRate, Resource.Type);
        set => SetMoney(value, StandardRate, "Change standard rate", (r, v) => r.StandardRate = v);
    }

    public string OvertimeRate
    {
        get => Resource is { Type: ResourceType.Work } r ? FormatRate(r.OvertimeRate, r.Type) : "";
        set => SetMoney(value, OvertimeRate, "Change overtime rate", (r, v) => r.OvertimeRate = v);
    }

    public string CostPerUse
    {
        get => Resource is { Type: not ResourceType.Cost } r ? r.CostPerUse.ToString("C", CultureInfo.CurrentCulture) : "";
        set => SetMoney(value, CostPerUse, "Change cost per use", (r, v) => r.CostPerUse = v);
    }

    public string AccrueAt
    {
        get => Resource is null ? "" : Accruals[(int)Resource.AccrueAt];
        set
        {
            int index = Array.IndexOf(Accruals, value);
            if (Resource is null || index < 0 || index == (int)Resource.AccrueAt)
                return;
            Edit("Change accrual", (_, r) => r.AccrueAt = (CostAccrual)index);
        }
    }

    /// <summary>Names of the project's base calendars, for the Base Calendar column.</summary>
    public IEnumerable<string> CalendarNames => Project.Calendars.Select(c => c.Name);

    public string BaseCalendar
    {
        get => Resource?.Calendar?.BaseCalendar?.Name ?? "";
        set
        {
            if (Resource is not { Type: ResourceType.Work } || value == BaseCalendar)
                return;
            var calendarIndex = Project.Calendars.FindIndex(c => c.Name == value);
            if (calendarIndex < 0)
                return;
            Edit("Change base calendar", (p, r) =>
            {
                var newBase = p.Calendars[calendarIndex];
                r.Calendar = (r.Calendar ?? new WorkingCalendar(r.Name, newBase)).Rebase(newBase, r.Name);
            });
        }
    }

    // ---- Helpers ----

    private void SetText(string value, string? current, string description, Action<Resource> apply)
    {
        if (Resource is null || value == current)
            return;
        Edit(description, (_, r) => apply(r));
    }

    private void SetMoney(string value, string current, string description, Action<Resource, decimal> apply)
    {
        if (Resource is null || value == current)
            return;
        if (!TryParseMoney(value, out var amount) || amount < 0)
        {
            _owner.ShowError($"'{value}' is not a valid amount.");
            OnPropertyChanged(nameof(StandardRate));
            OnPropertyChanged(nameof(OvertimeRate));
            OnPropertyChanged(nameof(CostPerUse));
            return;
        }
        Edit(description, (_, r) => apply(r, amount));
    }

    private void Edit(string description, Action<Project, Resource> change)
    {
        int id = Resource!.Id;
        _owner.TryEdit(description, p => change(p, p.GetResource(id) ?? throw new InvalidOperationException("The resource no longer exists.")));
    }

    public static string FormatRate(decimal rate, ResourceType type) => type switch
    {
        ResourceType.Work => rate.ToString("C", CultureInfo.CurrentCulture) + "/hr",
        ResourceType.Material => rate.ToString("C", CultureInfo.CurrentCulture),
        _ => "",
    };

    /// <summary>Accepts "50", "$50.00", "50/hr" and similar; per-day/week/year rates are converted to hourly.</summary>
    public static bool TryParseMoney(string text, out decimal amount)
    {
        var cleaned = text.Trim();
        decimal factor = 1;
        foreach (var (suffix, hoursPer) in new[] { ("/hr", 1m), ("/h", 1m), ("/d", 8m), ("/day", 8m), ("/w", 40m), ("/wk", 40m), ("/y", 2080m), ("/yr", 2080m) })
        {
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^suffix.Length];
                factor = 1 / hoursPer;
                break;
            }
        }
        var ok = decimal.TryParse(cleaned, NumberStyles.Currency, CultureInfo.CurrentCulture, out amount)
                 || decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out amount);
        amount = Math.Round(amount * factor, 4);
        return ok;
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
}
