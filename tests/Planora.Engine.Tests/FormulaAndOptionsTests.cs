using Planora.Engine.Calendars;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class FormulaAndOptionsTests
{
    private const int Day = 480;

    private static (Project P, ProjectTask Task) Sample()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var task = p.AddTask("Build", 3 * Day);
        task.FixedCost = 500;
        task.Deadline = new DateTime(2026, 1, 9, 17, 0, 0);
        ProjectScheduler.Schedule(p);
        return (p, task);
    }

    private static object? Eval(Project p, ProjectTask t, string formula) => Formula.Parse(formula).Evaluate(p, t);

    [Fact]
    public void ArithmeticAndPrecedence()
    {
        var (p, t) = Sample();
        Assert.Equal(7.0, Eval(p, t, "1 + 2 * 3"));
        Assert.Equal(9.0, Eval(p, t, "(1 + 2) * 3"));
        Assert.Equal(-2.0, Eval(p, t, "-(4 / 2)"));
        Assert.Equal(true, Eval(p, t, "2 > 1 And Not (1 = 2)"));
        Assert.Equal("a-b", Eval(p, t, "\"a\" & \"-\" & \"b\""));
    }

    [Fact]
    public void FieldReferencesAndFunctions()
    {
        var (p, t) = Sample();
        Assert.Equal(1440.0, Eval(p, t, "[Duration]"));
        Assert.Equal(3.0, Eval(p, t, "[Duration] / 480"));
        Assert.Equal(500.0, Eval(p, t, "[Fixed Cost]"));
        Assert.Equal("Over", Eval(p, t, "IIf([Cost] > 100, \"Over\", \"Under\")"));
        Assert.Equal(5.0, Eval(p, t, "Len([Task Name])"));
        Assert.Equal("BUI", Eval(p, t, "UCase(Left([Name], 3))"));
        Assert.Equal(2026.0, Eval(p, t, "Year([Start])"));
        Assert.Equal(960.0, Eval(p, t, "ProjDateDiff([Finish], [Deadline])"));
        Assert.Equal(true, Eval(p, t, "IsNull([Baseline Start])"));
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("Foo(1)")]
    [InlineData("\"open")]
    [InlineData("IIf(1, 2)")]
    public void SyntaxErrorsAreReported(string formula) =>
        Assert.Throws<FormulaException>(() => Formula.Parse(formula));

    [Fact]
    public void CalculatedCustomFieldIsReadOnlyAndSaved()
    {
        var (p, t) = Sample();
        p.CustomFields.Add(new CustomField("Number1", "Days") { Formula = "[Duration] / 480" });
        p.CustomFields.Add(new CustomField("Text2", "Budget status") { Formula = "IIf([Cost] > 1000, \"Over\", \"OK\")" });
        p.CustomFields.Add(new CustomField("Flag1", "Broken") { Formula = "[No Such Field] > 1" });

        Assert.Equal(3.0, TaskFields.Get(p, "Number1").Get(p, t));
        Assert.Equal("OK", TaskFields.Get(p, "Text2").Get(p, t));
        Assert.Null(TaskFields.Get(p, "Flag1").Get(p, t));
        Assert.True(TaskFields.Get(p, "Number1").IsCalculated);
        Assert.False(TaskFields.TrySetCustom(p, t, "Number1", "5"));

        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        ProjectScheduler.Schedule(loaded);
        Assert.Equal(3.0, TaskFields.Get(loaded, "Number1").Get(loaded, loaded.Tasks[0]));
    }

    [Fact]
    public void FormulasReferringToEachOtherInALoopFail()
    {
        var (p, t) = Sample();
        p.CustomFields.Add(new CustomField("Number1") { Formula = "[Number2] + 1" });
        p.CustomFields.Add(new CustomField("Number2") { Formula = "[Number1] + 1" });
        Assert.Null(TaskFields.Get(p, "Number1").Get(p, t));
    }

    [Fact]
    public void YearlyExceptionRepeats()
    {
        var cal = WorkingCalendar.CreateStandard();
        cal.AddHoliday("Christmas", new DateOnly(2026, 12, 25)).RepeatYearly = true;
        cal.AddException("Year end", new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 1)).RepeatYearly = true;

        Assert.False(cal.IsWorkingDay(new DateOnly(2026, 12, 25)));
        Assert.False(cal.IsWorkingDay(new DateOnly(2028, 12, 25)));
        Assert.True(cal.IsWorkingDay(new DateOnly(2025, 12, 25))); // before the first year
        Assert.False(cal.IsWorkingDay(new DateOnly(2029, 1, 1)));  // range crossing New Year
        Assert.True(cal.IsWorkingDay(new DateOnly(2029, 1, 2)));

        var p = new Project("x", new DateTime(2026, 1, 5, 8, 0, 0)) { };
        p.Calendar.Exceptions.AddRange(cal.Exceptions);
        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        Assert.False(loaded.Calendar.IsWorkingDay(new DateOnly(2030, 12, 25)));
    }

    [Fact]
    public void NewTaskDefaultsApply()
    {
        var p = new Project("x", new DateTime(2026, 1, 5, 8, 0, 0));
        p.Options.DefaultTaskType = TaskType.FixedDuration;
        p.Options.NewTasksEffortDriven = true;
        p.Options.NewTasksManuallyScheduled = true;

        var task = p.AddTask("New", Day);

        Assert.Equal(TaskType.FixedDuration, task.Type);
        Assert.True(task.IsEffortDriven);
        Assert.Equal(TaskMode.ManuallyScheduled, task.Mode);
        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        Assert.True(loaded.Options.NewTasksManuallyScheduled);
        Assert.Equal(TaskType.FixedDuration, loaded.Options.DefaultTaskType);
    }
}
