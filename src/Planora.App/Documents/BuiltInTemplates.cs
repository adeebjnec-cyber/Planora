using Planora.Engine.Model;

namespace Planora.App.Documents;

/// <summary>Templates that ship with Planora (user templates are .planoratemplate files in the templates folder).</summary>
public static class BuiltInTemplates
{
    private const int Day = 480;

    public static IReadOnlyList<(string Name, Func<DateTime, Project> Create)> All { get; } = new (string, Func<DateTime, Project>)[]
    {
        ("Blank Project", start => new Project("Project1", start)),
        ("Simple Project Plan", SimplePlan),
        ("Office Relocation", start =>
        {
            var p = SampleProject.Create(start);
            foreach (var t in p.Tasks)
            {
                t.PercentComplete = 0;
                t.ActualStart = null;
                t.ActualFinish = null;
            }
            return p;
        }),
        ("Software Release", SoftwareRelease),
    };

    private static Project SimplePlan(DateTime start)
    {
        var p = new Project("Project Plan", start);
        Build(p, new (string, int, int)[]
        {
            ("Initiation", 1, 0), ("Define goals and scope", 2, 2), ("Identify stakeholders", 2, 1), ("Project charter approved", 2, 0),
            ("Planning", 1, 0), ("Create work breakdown", 2, 3), ("Estimate effort and cost", 2, 2), ("Plan approved", 2, 0),
            ("Execution", 1, 0), ("Deliver work", 2, 15), ("Status reviews", 2, 15), ("Deliverables accepted", 2, 0),
            ("Closing", 1, 0), ("Lessons learned", 2, 1), ("Project closed", 2, 0),
        });
        return p;
    }

    private static Project SoftwareRelease(DateTime start)
    {
        var p = new Project("Software Release", start);
        Build(p, new (string, int, int)[]
        {
            ("Requirements", 1, 0), ("Gather requirements", 2, 5), ("Write specification", 2, 4), ("Requirements signed off", 2, 0),
            ("Design", 1, 0), ("Architecture", 2, 4), ("UI design", 2, 5), ("Design review", 2, 1),
            ("Development", 1, 0), ("Build features", 2, 20), ("Code review", 2, 5), ("Feature complete", 2, 0),
            ("Testing", 1, 0), ("Test plan", 2, 3), ("System testing", 2, 10), ("Bug fixing", 2, 8), ("Release candidate", 2, 0),
            ("Release", 1, 0), ("Documentation", 2, 4), ("Deploy", 2, 1), ("Released", 2, 0),
        });
        return p;
    }

    /// <summary>Adds tasks (name, outline level, days) and links each task to the previous task at the same level within a phase.</summary>
    private static void Build(Project p, IEnumerable<(string Name, int Level, int Days)> rows)
    {
        ProjectTask? previous = null;
        foreach (var (name, level, days) in rows)
        {
            var task = p.AddTask(name, level == 1 ? 0 : days * Day);
            if (level == 2)
                p.Indent(task);
            if (level == 2 && days == 0)
                task.IsMilestone = true;
            if (level == 2 && previous is not null && previous.OutlineLevel == 2)
                p.Link(previous, task);
            else if (level == 1 && previous is not null)
                p.Link(previous.Parent ?? previous, task);
            previous = task;
        }
    }
}
