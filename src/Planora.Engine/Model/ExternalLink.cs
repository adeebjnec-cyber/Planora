namespace Planora.Engine.Model;

/// <summary>
/// A predecessor in another project file (Microsoft Project's external or cross-project link). The other
/// task's dates are read when links are refreshed and kept here, so the schedule works offline and when
/// the other file is missing.
/// </summary>
public sealed class ExternalLink
{
    /// <summary>Full path of the other project file.</summary>
    public string ProjectPath { get; set; } = "";

    /// <summary>Unique ID of the predecessor task in that file.</summary>
    public int TaskId { get; set; }

    public string TaskName { get; set; } = "";
    public LinkType Type { get; set; } = LinkType.FinishToStart;

    /// <summary>Lag in working minutes on this project's calendar (negative = lead).</summary>
    public long LagMinutes { get; set; }

    /// <summary>The other task's dates when the link was last refreshed.</summary>
    public DateTime? Start { get; set; }
    public DateTime? Finish { get; set; }

    /// <summary>When the dates were last read, or null if never.</summary>
    public DateTime? RefreshedOn { get; set; }

    public ExternalLink Copy() => (ExternalLink)MemberwiseClone();
}
