namespace Planora.Engine.Model;

public enum LinkType
{
    FinishToStart,
    StartToStart,
    FinishToFinish,
    StartToFinish,
}

public enum ConstraintType
{
    AsSoonAsPossible,
    AsLateAsPossible,
    MustStartOn,
    MustFinishOn,
    StartNoEarlierThan,
    StartNoLaterThan,
    FinishNoEarlierThan,
    FinishNoLaterThan,
}

/// <summary>Which of duration, units and work stays fixed when one of the others is edited.</summary>
public enum TaskType
{
    FixedUnits,
    FixedDuration,
    FixedWork,
}

public enum TaskMode
{
    AutoScheduled,
    ManuallyScheduled,
}
