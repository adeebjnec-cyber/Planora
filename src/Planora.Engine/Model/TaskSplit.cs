namespace Planora.Engine.Model;

/// <summary>
/// An interruption in a task: after <see cref="AfterWorkMinutes"/> of the task's work, it pauses for
/// <see cref="GapMinutes"/> of working time. Splits keep their shape when the task moves.
/// <see cref="FromLeveling"/> marks splits made by resource leveling, which Clear Leveling removes.
/// </summary>
public readonly record struct TaskSplit(long AfterWorkMinutes, long GapMinutes, bool FromLeveling = false);

/// <summary>One continuous working piece of a (possibly split) task.</summary>
public readonly record struct TaskSegment(DateTime Start, DateTime Finish);
