using Planora.Engine.Model;
using Planora.Engine.Reports;

namespace Planora.Engine.Tests;

public class ReportTests
{
    private const int Day = 480;

    private static Project Sample()
    {
        var p = new Project("Plan", new DateTime(2026, 1, 5, 8, 0, 0));
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A", 2 * Day);
        var b = p.AddTask("B", Day);
        p.Indent(a);
        p.Indent(b);
        var done = p.AddTask("Done", 0);
        done.IsMilestone = true;
        p.Link(a, b);
        p.Link(phase, done);
        var r = p.AddResource("R");
        r.StandardRate = 10;
        p.Assign(a, r);
        p.Assign(b, r);
        p.StatusDate = new DateTime(2026, 1, 6, 17, 0, 0);
        return p;
    }

    [Theory]
    [MemberData(nameof(AllReports))]
    public void EveryReportBuilds(string name)
    {
        var report = ReportCatalog.Build(Sample(), name);
        Assert.Equal(name, report.Title);
        Assert.NotEmpty(report.Blocks);
    }

    public static IEnumerable<object[]> AllReports() => ReportCatalog.Names.Select(n => new object[] { n });

    [Fact]
    public void OverviewFigures()
    {
        var report = ReportCatalog.Build(Sample(), "Project Overview");
        var figures = report.Blocks.OfType<FiguresBlock>().First().Items.ToDictionary(i => i.Label, i => i.Value);
        Assert.Equal("3 days", figures["Duration"]);
        Assert.Equal("24 hrs", figures["Work"]);
        Assert.Equal("1", figures["Late tasks"]); // A should be half done by the status date
    }

    [Fact]
    public void CriticalTasksListsIncompleteCriticalWork()
    {
        var report = ReportCatalog.Build(Sample(), "Critical Tasks");
        var table = report.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(new[] { "A", "B", "Done" }, table.Rows.Select(r => r[0]));
    }

    [Fact]
    public void EmptyProjectReportsAreFriendly()
    {
        var p = new Project("Empty", new DateTime(2026, 1, 5, 8, 0, 0));
        foreach (var name in ReportCatalog.Names)
            Assert.NotEmpty(ReportCatalog.Build(p, name).Blocks);
    }
}
