using Planora.Engine.Calendars;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class ProjectFileTests
{
    private const int Day = 480;

    private static DateTime D(int day, int hour, int minute = 0) => new(2026, 1, day, hour, minute, 0);

    /// <summary>A project that touches every stored feature.</summary>
    private static Project RichProject()
    {
        var p = new Project("Office move", D(5, 8))
        {
            StatusDate = D(7, 8),
        };
        p.Options.MinutesPerDay = 450;
        p.Options.CalculateMultipleCriticalPaths = true;
        p.Calendar.AddHoliday("New office day", new DateOnly(2026, 1, 9));
        var nights = WorkingCalendar.CreateNightShift();
        p.Calendars.Add(nights);

        var phase = p.AddTask("Planning");
        var survey = p.AddTask("Survey", 2 * Day);
        var design = p.AddTask("Design", 3 * Day);
        p.Indent(survey);
        p.Indent(design);
        var link = p.Link(survey, design, LinkType.StartToStart);
        link.LagFormat = LagFormat.PercentOfPredecessor;
        link.LagPercent = 50;
        design.Split(Day, Day);
        design.Notes = "Two layouts";
        design.Deadline = D(20, 17);
        design.ConstraintType = ConstraintType.StartNoEarlierThan;
        design.ConstraintDate = D(6, 8);
        design.Priority = 0;
        design.FixedCost = 120.5m;
        design.FixedCostAccrual = CostAccrual.Start;

        var move = p.AddTask("Move", Day);
        move.IsActive = false;
        move.Calendar = nights;
        move.IsElapsedDuration = true;
        p.Link(phase, move, LinkType.FinishToStart, -60);

        p.AddRecurringTask("Weekly sync", new RecurrencePattern
        {
            Frequency = RecurrenceFrequency.Weekly,
            DaysOfWeek = { DayOfWeek.Monday },
            Start = new DateOnly(2026, 1, 5),
            Occurrences = 2,
            OccurrenceDurationMinutes = 30,
        });

        var mover = p.AddResource("Mover");
        mover.StandardRate = 40;
        mover.AddRateChange(0, new CostRate(D(12, 0), 45, 60, 10));
        mover.MaxUnits = 0;
        mover.AccrueAt = CostAccrual.Start;
        mover.Calendar!.AddHoliday("Sick", new DateOnly(2026, 1, 6));
        var boxes = p.AddResource("Boxes", ResourceType.Material);
        boxes.MaterialLabel = "box";
        var budget = p.AddResource("Budget", ResourceType.Cost);
        budget.IsBudget = true;
        p.BudgetCosts[budget] = 5000m;

        var a = p.Assign(survey, mover, 0.5);
        a.Contour = WorkContour.Bell;
        a.CostRateTable = 2;
        p.Assign(survey, boxes, 30);

        survey.ActualStart = D(5, 8);
        survey.PercentComplete = 40;

        ProjectScheduler.Schedule(p);
        p.SaveBaseline();
        p.SaveBaseline(4);
        p.SaveInterimPlan(2);
        return p;
    }

    [Fact]
    public void RoundTripPreservesTheSchedule()
    {
        var original = RichProject();
        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(original));
        ProjectScheduler.Schedule(loaded);

        Assert.Equal(original.Tasks.Count, loaded.Tasks.Count);
        foreach (var (o, l) in original.Tasks.Zip(loaded.Tasks))
        {
            Assert.Equal((o.Id, o.Name, o.WbsCode, o.OutlineLevel), (l.Id, l.Name, l.WbsCode, l.OutlineLevel));
            Assert.Equal((o.Start, o.Finish), (l.Start, l.Finish));
            Assert.Equal((o.TotalFloatMinutes, o.IsCritical, o.IsActive), (l.TotalFloatMinutes, l.IsCritical, l.IsActive));
            Assert.Equal((o.WorkMinutes, o.Cost, o.ActualCost), (l.WorkMinutes, l.Cost, l.ActualCost));
            Assert.Equal(o.Baselines, l.Baselines);
            Assert.Equal(o.InterimPlans, l.InterimPlans);
            Assert.Equal(o.Segments, l.Segments);
        }
        Assert.Equal(original.Finish, loaded.Finish);
        Assert.Equal(original.Cost, loaded.Cost);
    }

    [Fact]
    public void RoundTripPreservesDetails()
    {
        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(RichProject()));

        var design = loaded.Tasks.Single(t => t.Name == "Design");
        Assert.Equal("Two layouts", design.Notes);
        Assert.Equal(0, design.Priority);
        Assert.Equal(CostAccrual.Start, design.FixedCostAccrual);
        Assert.Equal(120.5m, design.FixedCost);
        Assert.Single(design.Splits);
        Assert.Equal(LagFormat.PercentOfPredecessor, design.Predecessors[0].LagFormat);
        Assert.Equal(50, design.Predecessors[0].LagPercent);

        var move = loaded.Tasks.Single(t => t.Name == "Move");
        Assert.False(move.IsActive);
        Assert.True(move.IsElapsedDuration);
        Assert.Equal("Night Shift", move.Calendar!.Name);
        Assert.Contains(move.Calendar, loaded.Calendars);
        Assert.Equal(-60, move.Predecessors[0].LagMinutes);

        var sync = loaded.Tasks.Single(t => t.Name == "Weekly sync");
        Assert.True(sync.IsRecurring);
        Assert.Equal(2, sync.Children.Count);

        var mover = loaded.Resources.Single(r => r.Name == "Mover");
        Assert.Equal(0, mover.MaxUnits);
        Assert.Equal(CostAccrual.Start, mover.AccrueAt);
        Assert.Equal(2, mover.CostRateTables[0].Count);
        Assert.Same(loaded.Calendar, mover.Calendar!.BaseCalendar);
        Assert.False(mover.Calendar.IsWorkingDay(new DateOnly(2026, 1, 6)));
        Assert.False(loaded.Calendar.IsWorkingDay(new DateOnly(2026, 1, 9)));

        var survey = loaded.Tasks.Single(t => t.Name == "Survey");
        Assert.Equal(WorkContour.Bell, survey.Assignments[0].Contour);
        Assert.Equal(2, survey.Assignments[0].CostRateTable);
        Assert.Equal(30, survey.Assignments[1].Units);
        Assert.Equal(40, survey.PercentComplete);

        Assert.Equal(5000m, loaded.BudgetCosts.Single().Value);
        Assert.Equal(450, loaded.Options.MinutesPerDay);
        Assert.True(loaded.Options.CalculateMultipleCriticalPaths);
        Assert.Equal(D(7, 8), loaded.StatusDate);
        Assert.NotNull(loaded.Baselines[0]);
    }

    [Fact]
    public void NewTasksGetFreshIdsAfterLoading()
    {
        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(RichProject()));
        int maxId = loaded.Tasks.Max(t => t.Id);
        Assert.Equal(maxId + 1, loaded.AddTask("New").Id);
        Assert.Equal(loaded.Resources.Max(r => r.Id) + 1, loaded.AddResource("New").Id);
    }

    [Fact]
    public void SaveAndLoadFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"planora-test-{Guid.NewGuid():N}{ProjectFile.Extension}");
        try
        {
            ProjectFile.Save(RichProject(), path);
            var loaded = ProjectFile.Load(path);
            Assert.Equal("Office move", loaded.Name);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"formatVersion\": 99}")]
    [InlineData("{\"formatVersion\": 1, \"tasks\": [{\"id\": 1, \"name\": \"A\", \"predecessors\": [{\"predecessorId\": 7}]}]}")]
    public void InvalidFilesAreRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => ProjectFile.Deserialize(json));
}
