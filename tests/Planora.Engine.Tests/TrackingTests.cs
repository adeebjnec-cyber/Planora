using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class TrackingTests
{
    private const int Day = 480;

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    private static (Project Project, ProjectTask Task) CostedProject()
    {
        var p = new Project("Test", D(5, 8));
        var task = p.AddTask("Job", 4 * Day);
        var r = p.AddResource("R");
        r.StandardRate = 50;
        p.Assign(task, r);
        ProjectScheduler.Schedule(p);
        return (p, task);
    }

    [Fact]
    public void BaselineCapturesPlan()
    {
        var (p, task) = CostedProject();

        p.SaveBaseline();

        var b = task.Baselines[0]!;
        Assert.Equal((D(5, 8), D(8, 17)), (b.Start, b.Finish));
        Assert.Equal(1600m, b.Cost);
        Assert.Equal(4 * Day, b.WorkMinutes);
        Assert.NotNull(p.Baselines[0]);
        Assert.NotNull(task.Assignments[0].Baselines[0]);
    }

    [Fact]
    public void VarianceAgainstBaseline()
    {
        var (p, task) = CostedProject();
        p.SaveBaseline();

        p.SetDuration(task, 5 * Day);
        ProjectScheduler.Schedule(p);

        Assert.Equal(Day, task.FinishVarianceMinutes);
        Assert.Equal(0, task.StartVarianceMinutes);
        Assert.Equal(Day, task.DurationVarianceMinutes);
        Assert.Equal(Day, task.WorkVarianceMinutes);
        Assert.Equal(400m, task.CostVariance);
    }

    [Fact]
    public void NoBaselineMeansNoVariance()
    {
        var (_, task) = CostedProject();
        Assert.Equal(0, task.FinishVarianceMinutes);
        Assert.Equal(0m, task.CostVariance);
    }

    [Fact]
    public void MultipleBaselinesAreIndependent()
    {
        var (p, task) = CostedProject();
        p.SaveBaseline(0);
        p.SetDuration(task, 5 * Day);
        ProjectScheduler.Schedule(p);
        p.SaveBaseline(3);

        Assert.Equal(D(8, 17), task.Baselines[0]!.Finish);
        Assert.Equal(D(9, 17), task.Baselines[3]!.Finish);

        p.ClearBaseline(0);
        Assert.Null(task.Baselines[0]);
        Assert.NotNull(task.Baselines[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => p.SaveBaseline(11));
    }

    [Fact]
    public void BaselineSelectedTasksWithRollUp()
    {
        var p = new Project("Test", D(5, 8));
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A", Day);
        var b = p.AddTask("B", 2 * Day);
        p.Indent(a);
        p.Indent(b);
        ProjectScheduler.Schedule(p);

        p.SaveBaseline(0, new[] { b }, rollUpToSummaries: true);

        Assert.Null(a.Baselines[0]);
        Assert.NotNull(phase.Baselines[0]);
        Assert.Equal(b.Finish, phase.Baselines[0]!.Finish);
        Assert.Null(p.Baselines[0]);
    }

    [Fact]
    public void InterimPlans()
    {
        var (p, task) = CostedProject();
        p.SaveBaseline();
        p.SetDuration(task, 5 * Day);
        ProjectScheduler.Schedule(p);

        p.SaveInterimPlan(0);
        p.SaveInterimPlan(1, fromBaseline: 0);

        Assert.Equal(new InterimPlan(D(5, 8), D(9, 17)), task.InterimPlans[0]);
        Assert.Equal(new InterimPlan(D(5, 8), D(8, 17)), task.InterimPlans[1]);
    }

    [Fact]
    public void EarnedValueAtStatusDate()
    {
        var (p, task) = CostedProject();
        p.SaveBaseline();
        p.StatusDate = D(7, 8); // two of four days planned
        task.ActualStart = D(5, 8);
        task.PercentComplete = 25;

        ProjectScheduler.Schedule(p);

        var ev = task.EarnedValue;
        Assert.Equal(800m, ev.Bcws);
        Assert.Equal(400m, ev.Bcwp);
        Assert.Equal(400m, ev.Acwp);
        Assert.Equal(1600m, ev.Bac);
        Assert.Equal(-400m, ev.Sv);
        Assert.Equal(0m, ev.Cv);
        Assert.Equal(0.5m, ev.Spi);
        Assert.Equal(1m, ev.Cpi);
        Assert.Equal(1600m, ev.Eac);
        Assert.Equal(ev, p.EarnedValue);
    }

    [Fact]
    public void BudgetsAreHeldPerBudgetResource()
    {
        var p = new Project("Test", D(5, 8));
        var travel = p.AddResource("Travel budget", ResourceType.Cost);
        travel.IsBudget = true;
        p.BudgetCosts[travel] = 10_000m;
        Assert.Equal(10_000m, p.BudgetCosts.Values.Sum());
    }
}
