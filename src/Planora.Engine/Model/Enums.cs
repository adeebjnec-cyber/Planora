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

public enum TaskMode
{
    AutoScheduled,
    ManuallyScheduled,
}
