using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;
using Planora.Engine.Views;

namespace Planora.Engine.Tests;

public class SpreadsheetFileTests
{
    private const int Day = 480;

    private static Project Sample()
    {
        var p = new Project("Plan", new DateTime(2026, 1, 5, 8, 0, 0));
        var phase = p.AddTask("Phase, one");
        var a = p.AddTask("Write \"spec\"", 2 * Day);
        var b = p.AddTask("Build", 3 * Day);
        p.Indent(a);
        p.Indent(b);
        var done = p.AddTask("Done", 0);
        done.IsMilestone = true;
        p.Link(a, b, LinkType.StartToStart, Day);
        p.Link(b, done);
        ResourceNamesText.Apply(p, b, "Alice, Bob[50%]");
        a.Notes = "Line one";
        ProjectScheduler.Schedule(p);
        return p;
    }

    private static string TempFile(string extension) => Path.Combine(Path.GetTempPath(), $"planora-{Guid.NewGuid():N}{extension}");

    private static TableDefinition ImportTable => new("Import",
        new TableColumn("Name"), new TableColumn("Duration"), new TableColumn("Start"), new TableColumn("Finish"),
        new TableColumn("Predecessors"), new TableColumn("ResourceNames"), new TableColumn("Milestone"), new TableColumn("Notes"));

    private static void AssertSameSchedule(Project original, Project imported)
    {
        Assert.Equal(original.Tasks.Select(t => (t.Name, t.OutlineLevel)), imported.Tasks.Select(t => (t.Name, t.OutlineLevel)));
        Assert.Equal(original.Tasks.Select(t => t.Finish), imported.Tasks.Select(t => t.Finish));
        var build = imported.Tasks.Single(t => t.Name == "Build");
        Assert.Equal(LinkType.StartToStart, build.Predecessors.Single().Type);
        Assert.Equal("Alice, Bob[50%]", ResourceNamesText.Format(build));
        Assert.True(imported.Tasks.Single(t => t.Name == "Done").IsMilestone);
        Assert.Equal("Line one", imported.Tasks.Single(t => t.Name.StartsWith("Write")).Notes);
    }

    [Fact]
    public void CsvRoundTrip()
    {
        var original = Sample();
        var path = TempFile(".csv");
        try
        {
            SpreadsheetFile.SaveCsv(SpreadsheetFile.Rows(original, ImportTable, original.Tasks), path);
            var imported = SpreadsheetFile.Import(SpreadsheetFile.LoadCsv(path), "Imported", original.StartDate);
            AssertSameSchedule(original, imported);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void XlsxRoundTrip()
    {
        var original = Sample();
        var path = TempFile(".xlsx");
        try
        {
            SpreadsheetFile.SaveXlsx(SpreadsheetFile.Rows(original, ImportTable, original.Tasks), path);
            var rows = SpreadsheetFile.LoadXlsx(path);
            Assert.Equal("Task Name", rows[0][2]);
            var imported = SpreadsheetFile.Import(rows, "Imported", original.StartDate);
            AssertSameSchedule(original, imported);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportMinimalSheet()
    {
        var rows = new List<string[]>
        {
            new[] { "Name", "Duration", "Predecessors" },
            new[] { "Dig", "2d", "" },
            new[] { "Pour", "1d", "1" },
            new[] { "", "", "" },
        };
        var p = SpreadsheetFile.Import(rows, "Pond", new DateTime(2026, 1, 5, 8, 0, 0));

        Assert.Equal(2, p.Tasks.Count);
        Assert.Equal(new DateTime(2026, 1, 7, 17, 0, 0), p.Finish);
    }

    [Fact]
    public void ImportWithoutNameColumnIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => SpreadsheetFile.Import(new List<string[]> { new[] { "Duration" }, new[] { "1d" } }, "x"));
    }

    [Fact]
    public void CsvQuotingIsPreserved()
    {
        var path = TempFile(".csv");
        try
        {
            SpreadsheetFile.SaveCsv(new[] { new[] { "a,b", "say \"hi\"", "two\nlines" } }, path);
            var rows = SpreadsheetFile.LoadCsv(path);
            Assert.Equal(new[] { "a,b", "say \"hi\"", "two\nlines" }, rows.Single().Select(c => c.Replace("\r", "")));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
