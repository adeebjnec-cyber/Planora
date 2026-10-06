namespace Planora.Engine.Model;

public static class Baseline
{
    /// <summary>Baseline plus Baseline1-Baseline10, as in Microsoft Project.</summary>
    public const int Count = 11;

    /// <summary>Interim plans Start1/Finish1 to Start10/Finish10.</summary>
    public const int InterimPlanCount = 10;
}

/// <summary>A saved snapshot of a task's or assignment's plan.</summary>
public sealed record BaselineValues(
    DateTime Start,
    DateTime Finish,
    long DurationMinutes,
    long WorkMinutes,
    decimal Cost,
    DateTime SavedOn);

/// <summary>An interim plan: just the start and finish dates.</summary>
public readonly record struct InterimPlan(DateTime Start, DateTime Finish);
