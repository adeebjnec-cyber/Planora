namespace Planora.Engine.Model;

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

    /// <summary>Lag in working minutes. Negative values are leads.</summary>
    public long LagMinutes { get; set; }
}
