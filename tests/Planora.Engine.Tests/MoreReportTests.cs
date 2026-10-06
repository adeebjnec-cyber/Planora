using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Reports;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class MoreReportTests
{
    private static Project Sample()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var bob = p.AddResource("Bob");
        bob.StandardRate = 10;
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A", 3 * 480);
        var b = p.AddTask("B", 2 * 480);
        p.Indent(a);
        p.Indent(b);
        p.Link(a, b);
        p.Assign(a, bob);
        p.Assign(b, bob);
        ProjectScheduler.Schedule(p);
        p.SaveBaseline();
        p.SetDuration(a, 5 * 480);     // slips and costs more than planned
        TaskProgress.SetPercentComplete(a, 40);
        p.StatusDate = new DateTime(2026, 1, 7, 17, 0, 0);
        ProjectScheduler.Schedule(p);
        return p;
    }

    [Fact]
    public void EveryBuiltInReportBuilds()
    {
        var p = Sample();
        foreach (var name in ReportCatalog.Names)
        {
            var report = ReportCatalog.Build(p, name);
            Assert.Equal(name, report.Title);
            Assert.NotEmpty(report.Blocks);
        }
    }

    [Fact]
    public void BurndownAndCashFlowAddUp()
    {
        var p = Sample();
        var cash = ReportCatalog.Build(p, "Cash Flow").Blocks.OfType<TimeChartBlock>().Single();
        var cumulative = cash.Series.Single(s => s.Name == "Cumulative cost").Values;
        Assert.Equal((double)p.Cost, cumulative[^1], 2);

        var burndown = ReportCatalog.Build(p, "Burndown").Blocks.OfType<TimeChartBlock>().First();
        var remaining = burndown.Series.Single(s => s.Name == "Remaining cumulative work").Values;
        Assert.Equal(0, remaining[^1], 3);
        Assert.Contains(burndown.Series, s => s.Name == "Baseline remaining work");

        var slipping = ReportCatalog.Build(p, "Slipping Tasks").Blocks.OfType<TableBlock>().Single();
        Assert.Contains(slipping.Rows, r => r[0] == "B");
        var overruns = ReportCatalog.Build(p, "Cost Overruns").Blocks.OfType<TableBlock>().First();
        Assert.Contains(overruns.Rows, r => r[0].Trim() == "A");
    }

    [Fact]
    public void CustomReportsBuildAndAreSaved()
    {
        var p = Sample();
        p.CustomReports.Add(new CustomReport
        {
            Name = "Mine",
            Elements =
            {
                new CustomReportElement { Kind = CustomElementKind.Text, Title = "Intro", Text = "Hello" },
                new CustomReportElement { Kind = CustomElementKind.TaskTable, Fields = { "Duration" } },
                new CustomReportElement { Kind = CustomElementKind.TaskChart, ChartField = "Work", SecondChartField = "BaselineWork", TopLevelOnly = false },
                new CustomReportElement { Kind = CustomElementKind.ResourceTable },
            },
        });
        Assert.Contains("Mine", ReportCatalog.AllNames(p));
        var report = ReportCatalog.Build(p, "Mine");
        Assert.Contains(report.Blocks, b => b is ParagraphBlock { Text: "Hello" });
        var table = report.Blocks.OfType<TableBlock>().First();
        Assert.Equal(new[] { "Task Name", "Start", "Finish", "% Complete", "Duration" }, table.Columns);
        var chart = report.Blocks.OfType<BarChartBlock>().Single();
        Assert.Equal(2, chart.Bars.Count);
        Assert.Equal(40, chart.Bars[0].Value, 3);      // A: 5 days = 40 hours

        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        Assert.Equal(4, loaded.CustomReports.Single().Elements.Count);
    }
}
