namespace Planora.Engine.Model;

/// <summary>
/// An interruption in a task: after <see cref="AfterWorkMinutes"/> of the task's work, it pauses for
/// <see cref="GapMinutes"/> of working time. Splits keep their shape when the task moves.
/// </summary>
public readonly record struct TaskSplit(long AfterWorkMinutes, long GapMinutes);

/// <summary>One continuous working piece of a (possibly split) task.</summary>
public readonly record struct TaskSegment(DateTime Start, DateTime Finish);
