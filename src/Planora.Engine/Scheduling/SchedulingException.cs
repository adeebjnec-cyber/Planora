using Planora.Engine.Model;

namespace Planora.Engine.Scheduling;

public sealed class SchedulingException : Exception
{
    public SchedulingException(string message, IReadOnlyList<ProjectTask> tasks)
        : base(message)
    {
        Tasks = tasks;
    }

    /// <summary>The tasks involved in the problem (e.g. those forming a dependency loop).</summary>
    public IReadOnlyList<ProjectTask> Tasks { get; }
}
