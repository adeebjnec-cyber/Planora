using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class ForeignFormatTests : IDisposable
{
    private const int Day = 480;
    private readonly List<string> _files = new();

    private static DateTime D(int day, int hour = 8) => new(2026, 1, day, hour, 0, 0);

    private string NewFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"planora-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _files.Where(File.Exists))
            File.Delete(file);
    }

    private static Project Sample()
    {
        var p = new Project("Bridge", D(5));
        var phase = p.AddTask("Phase");
        var a = p.AddTask("Survey", 2 * Day);
        var b = p.AddTask("Build", 3 * Day);
        p.Indent(a);
        p.Indent(b);
        p.Link(a, b);
        var done = p.AddTask("Open", 0);
        done.IsMilestone = true;
        p.Link(phase, done);
        p.Assign(b, p.AddResource("Crew"));
        ProjectScheduler.Schedule(p);
        return p;
    }

    [Theory]
    [InlineData(ForeignFormats.ExportFormat.Mpx, ".mpx")]
    [InlineData(ForeignFormats.ExportFormat.PrimaveraXml, ".xml")]
    [InlineData(ForeignFormats.ExportFormat.PrimaveraXer, ".xer")]
    public void ExportThenImportKeepsTheSchedule(ForeignFormats.ExportFormat format, string extension)
    {
        var original = Sample();
        var path = NewFile(extension);

        ForeignFormats.Save(original, path, format);
        var loaded = ForeignFormats.Load(path);
        ProjectScheduler.Schedule(loaded);

        var leaves = loaded.Tasks.Where(t => !t.IsSummary).ToList();
        Assert.Contains(leaves, t => t.Name == "Survey");
        Assert.Contains(leaves, t => t.Name == "Build");
        var build = leaves.Single(t => t.Name == "Build");
        Assert.Equal(3 * Day, build.DurationMinutes);
        Assert.Equal(D(7), build.Start);
        Assert.Equal(D(9, 17), build.Finish);
    }

    [Fact]
    public void UnreadableFilesAreRejected()
    {
        var path = NewFile(".mpp");
        File.WriteAllText(path, "not a project");
        Assert.Throws<InvalidDataException>(() => ForeignFormats.Load(path));
    }
}
