using Planora.Engine.Calendars;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class MspdiSchemaTests
{
    /// <summary>A project using most features that end up in Microsoft Project XML.</summary>
    private static Project Rich(bool split = true)
    {
        var p = new Project("Rich", new DateTime(2026, 1, 5, 8, 0, 0)) { StatusDate = new DateTime(2026, 1, 9, 17, 0, 0) };
        p.Options.RescheduleUncompletedWorkToStatusDate = true;
        var cal = p.Calendar;
        cal.AddHoliday("Holiday", new DateOnly(2026, 1, 1)).RepeatYearly = true;
        var week = new WorkWeek("Summer", new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31));
        week.SetDay(DayOfWeek.Friday);
        cal.WorkWeeks.Add(week);

        var bob = p.AddResource("Bob");
        bob.StandardRate = 20;
        bob.IsGeneric = true;
        bob.Availability.Add(new AvailabilityPeriod(new DateTime(2026, 2, 1), new DateTime(2026, 3, 1), 0.5));
        p.AddResource("Crates", ResourceType.Material).MaterialLabel = "box";
        p.AddResource("Travel", ResourceType.Cost);

        var phase = p.AddTask("Phase");
        var a = p.AddTask("A", 960);
        var b = p.AddTask("B", 480);
        var m = p.AddTask("Done", 0);
        p.Indent(a);
        p.Indent(b);
        p.Indent(m);
        p.Link(a, b, LinkType.FinishToStart, 60);
        p.Link(b, m);
        a.Hyperlink = "Spec";
        a.HyperlinkAddress = "https://example.com";
        a.IsMarked = true;
        a.Deadline = new DateTime(2026, 1, 20, 17, 0, 0);
        a.CustomValues["Text1"] = "x";
        a.CustomValues["Number2"] = 3.0;
        a.FixedCost = 10;
        b.ConstraintType = ConstraintType.StartNoEarlierThan;
        b.ConstraintDate = new DateTime(2026, 1, 8, 8, 0, 0);
        b.Mode = TaskMode.ManuallyScheduled;
        p.Assign(a, bob).OvertimeWorkMinutes = 60;
        p.Assign(a, p.Resources[1], 3);
        p.Assign(b, p.Resources[2]).CostAmount = 100;
        if (split)
            a.Split(240, 480);
        p.CustomFields.Add(new Fields.CustomField("Text1", "Area"));
        ProjectScheduler.Schedule(p);
        p.SaveBaseline();
        TaskProgress.SetPercentComplete(a, 30);
        ProjectScheduler.Schedule(p);
        return p;
    }

    [Fact]
    public void WrittenXmlFollowsMicrosoftProjectsSchema()
    {
        var doc = MspdiFile.Write(Rich());
        // Normalize again: a schema-correct document needs no changes.
        var before = doc.ToString();
        var removed = MspdiSchema.Normalize(doc);
        Assert.Empty(removed);
        Assert.Equal(before, doc.ToString());
    }

    [Fact]
    public void SchemaOrderingStillRoundTrips()
    {
        var p = Rich();
        var loaded = MspdiFile.Read(MspdiFile.Write(p));
        ProjectScheduler.Schedule(loaded);
        Assert.Equal(p.Tasks.Select(t => (t.Name, t.Start, t.Finish)), loaded.Tasks.Select(t => (t.Name, t.Start, t.Finish)));
        Assert.Equal(p.Calendar.WorkWeeks.Count, loaded.Calendar.WorkWeeks.Count);
        Assert.Equal("x", loaded.Tasks[1].CustomValues["Text1"]);
    }

    [Fact]
    public void SplitsRoundTripAsTimephasedWorkThatMpxjReads()
    {
        var p = new Project("Split", new DateTime(2026, 1, 5, 8, 0, 0));
        var bob = p.AddResource("Bob");
        var assigned = p.AddTask("Assigned", 3 * 480);
        var alone = p.AddTask("Unassigned", 2 * 480);
        p.Assign(assigned, bob);
        assigned.Split(480, 480);
        alone.Split(240, 960);
        ProjectScheduler.Schedule(p);
        TaskProgress.SetPercentComplete(assigned, 20);
        ProjectScheduler.Schedule(p);

        var loaded = MspdiFile.Read(MspdiFile.Write(p));
        ProjectScheduler.Schedule(loaded);
        Assert.Equal(p.Tasks.Select(t => t.Segments.ToList()), loaded.Tasks.Select(t => t.Segments.ToList()));

        var path = Path.Combine(Path.GetTempPath(), $"planora-split-{Guid.NewGuid():N}.xml");
        try
        {
            MspdiFile.Save(p, path);
            var file = new org.mpxj.mspdi.MSPDIReader().read(path);
            foreach (var task in p.Tasks)
            {
                var mpxj = file.getTasks().toArray().OfType<org.mpxj.Task>().First(x => x.getName() == task.Name);
                var splits = mpxj.getWorkSplits().toArray().OfType<org.mpxj.LocalDateTimeRange>().ToList();
                Assert.Equal(task.Segments.Count, splits.Count);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
