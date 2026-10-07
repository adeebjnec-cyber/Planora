using System.Xml;
using System.Xml.Linq;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

/// <summary>
/// The user's "MS Project Advanced Construction Test Pack": Microsoft Project XML written by another tool,
/// with assignments without work, progress without actual dates, links to the project summary task and
/// to missing tasks, and a parser edge-case file.
/// </summary>
public class ConstructionTestPackTests
{
    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    public static IEnumerable<object[]> ConstructionFiles() => new[]
    {
        new object[] { "G8_Commercial_Building_Advanced.xml" },
        new object[] { "Hospital_Construction_Advanced.xml" },
        new object[] { "Residential_Township_Advanced.xml" },
        new object[] { "Road_Highway_Advanced.xml" },
    };

    [Theory]
    [MemberData(nameof(ConstructionFiles))]
    public void ConstructionProjectsImportCompletely(string file)
    {
        var notes = new List<string>();
        var p = MspdiFile.Load(Data(file), notes);
        ProjectScheduler.Schedule(p);
        var doc = XDocument.Load(Data(file));
        XNamespace ns = doc.Root!.Name.Namespace;
        var fileTasks = doc.Descendants(ns + "Task").Where(t => (int)t.Element(ns + "OutlineLevel")! > 0).ToDictionary(t => (int)t.Element(ns + "UID")!);

        // Every task but the project summary row, with the file's durations.
        Assert.Equal(fileTasks.Count, p.Tasks.Count);
        foreach (var task in p.Tasks.Where(t => !t.IsSummary))
            Assert.Equal(XmlConvert.ToTimeSpan(fileTasks[task.Id].Element(ns + "Duration")!.Value).TotalMinutes, task.DurationMinutes);

        // Assignments without a Work value get duration × units, so work and cost are not zero.
        Assert.True(p.Assignments.Where(a => a.IsWork).All(a => a.WorkMinutes > 0));
        Assert.True(p.WorkMinutes > 0);
        Assert.True(p.Cost > 0);

        // Completed tasks keep the file's dates as their actual dates.
        foreach (var task in p.Tasks.Where(t => !t.IsSummary && t.PercentComplete >= 100))
        {
            Assert.Equal(DateTime.Parse(fileTasks[task.Id].Element(ns + "Start")!.Value), task.Start);
            Assert.Equal(DateTime.Parse(fileTasks[task.Id].Element(ns + "Finish")!.Value), task.Finish);
        }

        // The two links that cannot be imported are reported, not silently lost.
        Assert.Contains(notes, n => n.Contains("project summary task"));
        Assert.Contains(notes, n => n.Contains("task UID 100"));
        Assert.Equal(doc.Descendants(ns + "PredecessorLink").Count() - 2, p.Tasks.Sum(t => t.Predecessors.Count));
        Assert.Contains(notes, n => n.Contains("no work"));

        // Unstarted work follows the links on the file's 5-day calendar: no task starts before its predecessor finishes.
        foreach (var task in p.Tasks.Where(t => t.PercentComplete == 0 && !t.IsSummary))
        {
            foreach (var link in task.Predecessors.Where(l => l.Type == LinkType.FinishToStart && l.LagMinutes == 0))
                Assert.True(task.Start >= p.GetRequiredTask(link.PredecessorId).Finish);
        }
    }

    [Fact]
    public void ParserEdgeCases()
    {
        var notes = new List<string>();
        var p = MspdiFile.Load(Data("XML_Parser_Edge_Cases.xml"), notes);
        ProjectScheduler.Schedule(p);

        // UID 0 at outline level 1 is an ordinary task, not Microsoft Project's summary row.
        Assert.Equal(5, p.Tasks.Count);
        Assert.Equal("Zero Duration Milestone", p.Tasks[0].Name);
        Assert.True(p.Tasks[0].IsSummary);                          // the rows below it are at level 2
        Assert.Equal("Task With Special & Characters", p.Tasks[3].Name);
        Assert.True(p.Tasks[1].IsMilestone);

        // The manual task keeps its dates; the calendar the file names is missing.
        var manual = p.Tasks[4];
        Assert.Equal(TaskMode.ManuallyScheduled, manual.Mode);
        Assert.Equal(new DateTime(2027, 2, 23, 8, 0, 0), manual.Start);
        Assert.Contains(notes, n => n.Contains("project calendar"));

        // The round trip through Planora's own XML keeps everything.
        var again = MspdiFile.Read(MspdiFile.Write(p));
        Assert.Equal(p.Tasks.Select(t => t.Name), again.Tasks.Select(t => t.Name));
    }
}
