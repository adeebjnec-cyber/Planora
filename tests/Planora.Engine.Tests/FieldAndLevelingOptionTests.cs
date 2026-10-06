using Planora.Engine.Fields;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class FieldAndLevelingOptionTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    private static (Project, Resource, ProjectTask, ProjectTask) TwoTasksOneResource()
    {
        var p = new Project("Test", D(5, 8));
        var bob = p.AddResource("Bob");
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", 2 * Day);
        p.Assign(a, bob);
        p.Assign(b, bob);
        return (p, bob, a, b);
    }

    [Fact]
    public void ResourcesThatCannotLevelAreLeftAlone()
    {
        var (p, bob, a, b) = TwoTasksOneResource();
        bob.CanLevel = false;
        var result = ResourceLeveler.Level(p);
        Assert.Empty(result.DelayedTasks);
        Assert.Equal(a.Start, b.Start);
    }

    [Fact]
    public void LevelingOnlyChosenResources()
    {
        var (p, bob, a, b) = TwoTasksOneResource();
        var ann = p.AddResource("Ann");
        var c = p.AddTask("C", Day);
        var d = p.AddTask("D", Day);
        p.Assign(c, ann);
        p.Assign(d, ann);

        ResourceLeveler.Level(p, new LevelingOptions { Resources = new[] { ann } });
        Assert.Equal(a.Start, b.Start);        // Bob untouched
        Assert.NotEqual(c.Start, d.Start);     // Ann leveled
    }

    [Fact]
    public void LevelingDelaysOnlyTheOverallocatedAssignment()
    {
        var p = new Project("Test", D(5, 8));
        var bob = p.AddResource("Bob");
        var ann = p.AddResource("Ann");
        var solo = p.AddTask("Solo", 2 * Day);
        var pair = p.AddTask("Pair", 2 * Day);
        p.Assign(solo, bob);
        p.Assign(pair, ann);
        var bobOnPair = p.Assign(pair, bob);
        solo.Priority = 900;   // keep Solo in place

        ResourceLeveler.Level(p, new LevelingOptions { Order = LevelingOrder.PriorityStandard });

        Assert.Equal(D(5, 8), pair.Start);                       // Ann still starts on day one
        Assert.True(bobOnPair.LevelingDelayMinutes > 0);
        Assert.Equal(D(7, 8), bobOnPair.Start);                  // Bob joins when Solo is done
        Assert.Empty(ResourceAnalysis.FindOverallocations(p, OverallocationBasis.DayByDay));

        ResourceLeveler.ClearLeveling(p);
        Assert.Equal(0, bobOnPair.LevelingDelayMinutes);
    }

    [Fact]
    public void NewTaskAndResourceFieldsSurviveSaving()
    {
        var p = new Project("Test", D(5, 8));
        var t = p.AddTask("A", Day);
        t.Hyperlink = "Spec";
        t.HyperlinkAddress = "https://example.com/spec";
        t.HyperlinkSubAddress = "section-2";
        t.IsMarked = true;
        t.HideBar = true;
        var r = p.AddResource("Developer");
        r.IsGeneric = true;
        r.BookingType = BookingType.Proposed;
        r.CanLevel = false;

        foreach (var loaded in new[] { ProjectFile.Deserialize(ProjectFile.Serialize(p)), MspdiFile.Read(MspdiFile.Write(p)) })
        {
            var lt = loaded.Tasks[0];
            Assert.Equal(("Spec", "https://example.com/spec", "section-2"), (lt.Hyperlink, lt.HyperlinkAddress, lt.HyperlinkSubAddress));
            Assert.True(lt.HideBar);
            var lr = loaded.Resources[0];
            Assert.True(lr.IsGeneric);
            Assert.Equal(BookingType.Proposed, lr.BookingType);
            Assert.False(lr.CanLevel);
        }
        Assert.Equal(true, TaskFields.Get(p, "Marked").Get(p, t));
        // Marked is kept in .planora files; Microsoft Project XML has no element for it.
        Assert.True(ProjectFile.Deserialize(ProjectFile.Serialize(p)).Tasks[0].IsMarked);
    }
}
