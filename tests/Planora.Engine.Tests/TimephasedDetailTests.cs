using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class TimephasedDetailTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour = 0) => new(2026, 1, day, hour, 0, 0);

    [Fact]
    public void ActualAndBaselineWorkAreSpreadOverTheirDates()
    {
        var p = new Project("Test", D(5, 8));
        var bob = p.AddResource("Bob");
        bob.StandardRate = 10;
        var t = p.AddTask("A", 4 * Day);
        var a = p.Assign(t, bob);
        ProjectScheduler.Schedule(p);
        p.SaveBaseline();
        TaskProgress.SetPercentComplete(t, 50);
        ProjectScheduler.Schedule(p);

        Assert.Equal(480, Timephased.ActualWorkBetween(a, D(5), D(6)), 3);
        Assert.Equal(480, Timephased.ActualWorkBetween(a, D(6), D(7)), 3);
        Assert.Equal(0, Timephased.ActualWorkBetween(a, D(7), D(8)), 3);
        Assert.Equal(960, Timephased.BaselineWorkBetween(a, 0, D(5), D(7)), 3);
        Assert.Equal(160m, Timephased.BaselineCostBetween(a, 0, D(5), D(7)));
        Assert.Equal(80m, Timephased.ActualCostBetween(a, D(5), D(6)));
    }

    [Fact]
    public void FixedCostFollowsItsAccrual()
    {
        var p = new Project("Test", D(5, 8));
        var t = p.AddTask("A", 2 * Day);
        t.FixedCost = 100;
        ProjectScheduler.Schedule(p);
        Assert.Equal(50m, Timephased.FixedCostBetween(t, p.Calendar, D(5), D(6)));
        t.FixedCostAccrual = CostAccrual.End;
        Assert.Equal(0m, Timephased.FixedCostBetween(t, p.Calendar, D(5), D(6)));
        Assert.Equal(100m, Timephased.FixedCostBetween(t, p.Calendar, D(6), D(7)));
    }
}
