using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class TimephasedEditTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour = 0) => new(2026, 1, day, hour, 0, 0);

    private static (Project P, ProjectTask Task, Assignment A) TwoDayTask()
    {
        var p = new Project("Test", D(5, 8));
        var task = p.AddTask("Job", 2 * Day);
        var a = p.Assign(task, p.AddResource("R"));
        ProjectScheduler.Schedule(p);
        return (p, task, a);
    }

    private static double Hours(Assignment a, int day) => Timephased.WorkBetween(a, D(day), D(day + 1)) / 60;

    [Fact]
    public void EditingADayKeepsTheOtherDays()
    {
        var (p, task, a) = TwoDayTask();

        p.SetTimephasedWork(a, D(6), D(7), 4 * 60);
        ProjectScheduler.Schedule(p);

        Assert.Equal(WorkContour.Contoured, a.Contour);
        Assert.Equal(8, Hours(a, 5), 3);
        Assert.Equal(4, Hours(a, 6), 3);
        Assert.Equal(12 * 60, a.WorkMinutes);
        Assert.Equal(12 * 60, task.WorkMinutes);
        Assert.Equal(D(6, 17), task.Finish);
    }

    [Fact]
    public void AddingWorkAfterTheFinishExtendsTheTask()
    {
        var (p, task, a) = TwoDayTask();

        p.SetTimephasedWork(a, D(7), D(8), 6 * 60);
        ProjectScheduler.Schedule(p);

        Assert.Equal(6, Hours(a, 7), 3);
        Assert.Equal(22 * 60, a.WorkMinutes);
        Assert.Equal(D(7, 17), task.Finish);
        Assert.Equal(3 * Day, task.DurationMinutes);
    }

    [Fact]
    public void ZeroingTheLastDayShortensTheTask()
    {
        var (p, task, a) = TwoDayTask();

        p.SetTimephasedWork(a, D(6), D(7), 0);
        ProjectScheduler.Schedule(p);

        Assert.Equal(D(5, 17), task.Finish);
        Assert.Equal(8 * 60, a.WorkMinutes);
    }

    [Fact]
    public void MovingWorkResolvesOverallocation()
    {
        var p = new Project("Test", D(5, 8));
        var r = p.AddResource("R");
        p.Assign(p.AddTask("A", Day), r);
        var b = p.Assign(p.AddTask("B", Day), r);
        ProjectScheduler.Schedule(p);
        Assert.True(r.IsOverallocated);

        p.SetTimephasedWork(b, D(5), D(6), 0);
        p.SetTimephasedWork(b, D(6), D(7), 8 * 60);
        ProjectScheduler.Schedule(p);

        Assert.False(r.IsOverallocated);
        Assert.Equal(8, Hours(b, 6), 3);
    }

    [Fact]
    public void EditingAContouredAssignmentAgainKeepsEarlierEdits()
    {
        var (p, _, a) = TwoDayTask();
        p.SetTimephasedWork(a, D(5), D(6), 2 * 60);
        ProjectScheduler.Schedule(p);
        p.SetTimephasedWork(a, D(6), D(7), 3 * 60);
        ProjectScheduler.Schedule(p);

        Assert.Equal(2, Hours(a, 5), 3);
        Assert.Equal(3, Hours(a, 6), 3);
    }

    [Fact]
    public void TotalEditsGoBackToFlat()
    {
        var (p, _, a) = TwoDayTask();
        p.SetTimephasedWork(a, D(6), D(7), 4 * 60);
        p.SetWork(a, 16 * 60);
        Assert.Equal(WorkContour.Flat, a.Contour);
        Assert.Empty(a.Pieces);
    }

    [Fact]
    public void ContouredWorkIsSaved()
    {
        var (p, task, a) = TwoDayTask();
        p.SetTimephasedWork(a, D(6), D(7), 4 * 60);
        ProjectScheduler.Schedule(p);

        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        ProjectScheduler.Schedule(loaded);
        var la = loaded.Tasks[0].Assignments[0];

        Assert.Equal(a.Pieces, la.Pieces);
        Assert.Equal(4, Hours(la, 6), 3);
        Assert.Equal(task.Finish, loaded.Tasks[0].Finish);
    }

    [Fact]
    public void ContouredWorkMovesWithTheTask()
    {
        var (p, task, a) = TwoDayTask();
        p.SetTimephasedWork(a, D(6), D(7), 4 * 60);
        task.ConstraintType = ConstraintType.StartNoEarlierThan;
        task.ConstraintDate = D(12, 8);
        ProjectScheduler.Schedule(p);

        Assert.Equal(8, Hours(a, 12), 3);
        Assert.Equal(4, Hours(a, 13), 3);
    }

    [Fact]
    public void MaterialIsSpreadOverTheTask()
    {
        var p = new Project("Test", D(5, 8));
        var task = p.AddTask("Pack", 2 * Day);
        p.Assign(task, p.AddResource("Movers"));
        var crates = p.Assign(task, p.AddResource("Crates", ResourceType.Material), 40);
        ProjectScheduler.Schedule(p);

        Assert.Equal(20, Timephased.MaterialBetween(crates, D(5), D(6)), 3);
        Assert.Equal(40, Timephased.MaterialBetween(crates, D(1), D(10)), 3);
    }

    [Fact]
    public void InvalidEditsAreRejected()
    {
        var (p, task, a) = TwoDayTask();
        Assert.Throws<InvalidOperationException>(() => p.SetTimephasedWork(a, D(1), D(2), 60));      // before start
        Assert.Throws<InvalidOperationException>(() => p.SetTimephasedWork(a, D(10), D(11), 60));    // Saturday
        var fee = p.Assign(task, p.AddResource("Fee", ResourceType.Cost));
        Assert.Throws<InvalidOperationException>(() => p.SetTimephasedWork(fee, D(5), D(6), 60));
    }
}
