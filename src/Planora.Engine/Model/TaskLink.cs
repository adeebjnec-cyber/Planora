namespace Planora.Engine.Model;

public enum LagFormat
{
    /// <summary>Lag measured in working time on the project calendar.</summary>
    WorkingTime,

    /// <summary>Lag measured in continuous clock time (e.g. "2ed" for concrete curing).</summary>
    ElapsedTime,

    /// <summary>Lag as a percentage of the predecessor's duration.</summary>
    PercentOfPredecessor,
}

/// <summary>A dependency stored on the successor task, pointing at its predecessor.</summary>
public sealed class TaskLink
{
    public TaskLink(int predecessorId, LinkType type = LinkType.FinishToStart, long lagMinutes = 0)
    {
        PredecessorId = predecessorId;
        Type = type;
        LagMinutes = lagMinutes;
    }

    public int PredecessorId { get; }
    public LinkType Type { get; set; }
    public LagFormat LagFormat { get; set; } = LagFormat.WorkingTime;

    /// <summary>Lag in minutes (working or elapsed, per <see cref="LagFormat"/>). Negative values are leads.</summary>
    public long LagMinutes { get; set; }

    /// <summary>Lag as a percentage of the predecessor's duration, used when <see cref="LagFormat"/> is a percentage. May be negative.</summary>
    public double LagPercent { get; set; }
}
