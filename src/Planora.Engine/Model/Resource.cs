using Planora.Engine.Calendars;

namespace Planora.Engine.Model;

public enum ResourceType
{
    /// <summary>People and equipment that do work over time, paid per hour.</summary>
    Work,

    /// <summary>Consumables (concrete, steel), paid per unit of material.</summary>
    Material,

    /// <summary>A cost that does not depend on work or duration (travel, fees); the amount is entered per assignment.</summary>
    Cost,
}

/// <summary>When a cost is incurred, for actual-cost calculations.</summary>
public enum CostAccrual
{
    Start,
    Prorated,
    End,
}

/// <summary>
/// One row of a cost rate table, effective from a date until the next row. Rates for work resources
/// are per hour; for material resources they are per unit of material.
/// </summary>
/// <summary>Units available from <see cref="From"/> until <see cref="To"/> (open-ended when null). 1.0 = 100%.</summary>
public readonly record struct AvailabilityPeriod(DateTime From, DateTime? To, double Units);

public readonly record struct CostRate(DateTime EffectiveFrom, decimal StandardRate, decimal OvertimeRate = 0, decimal CostPerUse = 0);

public sealed class Resource
{
    /// <summary>Microsoft Project has five rate tables, A to E.</summary>
    public const int RateTableCount = 5;

    private double _maxUnits = 1.0;

    internal Resource(int id, string name, ResourceType type, WorkingCalendar? calendar)
    {
        Id = id;
        Name = name;
        Type = type;
        Calendar = calendar;
        for (int i = 0; i < RateTableCount; i++)
            CostRateTables[i] = new List<CostRate> { new(DateTime.MinValue, 0) };
    }

    public int Id { get; }
    public string Name { get; set; }
    public ResourceType Type { get; set; }
    public string Initials { get; set; } = "";
    public string Group { get; set; } = "";
    public string Code { get; set; } = "";
    public string Email { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>Unit label for material resources, e.g. "tons".</summary>
    public string MaterialLabel { get; set; } = "";

    /// <summary>Maximum units available at once (1.0 = 100%, one full-time person).</summary>
    public double MaxUnits
    {
        get => _maxUnits;
        set => _maxUnits = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Max units cannot be negative.");
    }

    /// <summary>
    /// Availability that varies over time (Microsoft Project's Resource Availability table): within a
    /// period, the period's units replace <see cref="MaxUnits"/>. Periods should not overlap.
    /// </summary>
    public List<AvailabilityPeriod> Availability { get; } = new();

    /// <summary>Maximum units available at a moment, from the availability table or <see cref="MaxUnits"/>.</summary>
    public double MaxUnitsAt(DateTime moment)
    {
        foreach (var period in Availability)
        {
            if (moment >= period.From && (period.To is null || moment < period.To))
                return period.Units;
        }
        return MaxUnits;
    }

    /// <summary>The resource's own working calendar; null means the project calendar.</summary>
    public WorkingCalendar? Calendar { get; set; }

    public CostAccrual AccrueAt { get; set; } = CostAccrual.Prorated;

    /// <summary>Budget resources are assigned only at project level to hold budget amounts.</summary>
    public bool IsBudget { get; set; }

    /// <summary>Cost rate tables A-E, each ordered by <see cref="CostRate.EffectiveFrom"/>.</summary>
    public List<CostRate>[] CostRateTables { get; } = new List<CostRate>[RateTableCount];

    /// <summary>Standard rate in table A from its first row (the common case of a single rate).</summary>
    public decimal StandardRate
    {
        get => CostRateTables[0][0].StandardRate;
        set => CostRateTables[0][0] = CostRateTables[0][0] with { StandardRate = value };
    }

    public decimal OvertimeRate
    {
        get => CostRateTables[0][0].OvertimeRate;
        set => CostRateTables[0][0] = CostRateTables[0][0] with { OvertimeRate = value };
    }

    public decimal CostPerUse
    {
        get => CostRateTables[0][0].CostPerUse;
        set => CostRateTables[0][0] = CostRateTables[0][0] with { CostPerUse = value };
    }

    /// <summary>The rate in effect at a moment.</summary>
    public CostRate RateAt(int table, DateTime moment)
    {
        var rates = CostRateTables[table];
        var rate = rates[0];
        foreach (var r in rates)
        {
            if (r.EffectiveFrom <= moment)
                rate = r;
        }
        return rate;
    }

    /// <summary>Adds a dated rate change to a table, keeping the table ordered.</summary>
    public void AddRateChange(int table, CostRate rate)
    {
        var rates = CostRateTables[table];
        rates.RemoveAll(r => r.EffectiveFrom == rate.EffectiveFrom);
        rates.Add(rate);
        rates.Sort((a, b) => a.EffectiveFrom.CompareTo(b.EffectiveFrom));
    }

    // ---- Computed by the scheduler ----

    public long WorkMinutes { get; internal set; }
    public decimal Cost { get; internal set; }
    public decimal ActualCost { get; internal set; }
    public double PeakUnits { get; internal set; }
    public bool IsOverallocated { get; internal set; }

    public override string ToString() => $"{Id}: {Name}";
}
