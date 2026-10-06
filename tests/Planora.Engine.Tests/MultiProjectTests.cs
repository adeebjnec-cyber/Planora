using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class MultiProjectTests : IDisposable
{
    private const int Day = 480;
    private readonly List<string> _files = new();

    // 2026-01-05 is a Monday.
    private static DateTime D(int day, int hour = 8) => new(2026, 1, day, hour, 0, 0);

    private string NewFile(string extension = ".planora")
    {
        var path = Path.Combine(Path.GetTempPath(), $"planora-multi-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _files.Where(File.Exists))
            File.Delete(file);
    }

    [Fact]
    public void ExternalPredecessorDrivesTheTaskAndRefreshes()
    {
        var supplier = new Project("Supplier", D(5));
        var delivery = supplier.AddTask("Deliver parts", 3 * Day);
        var supplierPath = NewFile();
        ProjectFile.Save(supplier, supplierPath);

        var p = new Project("Assembly", D(5));
        var assemble = p.AddTask("Assemble", 2 * Day);
        ExternalLinks.Add(assemble, supplierPath, delivery.Id);
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(8), assemble.Start);              // after Wed 7th 17:00

        // The supplier slips by two days; refreshing picks it up.
        supplier.SetDuration(delivery, 5 * Day);
        ProjectFile.Save(supplier, supplierPath);
        Assert.Empty(ExternalLinks.Refresh(p));
        ProjectScheduler.Schedule(p);
        Assert.Equal(D(12), assemble.Start);

        // Saved in .planora and Microsoft Project XML (as a cross-project link).
        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        Assert.Equal(supplierPath, loaded.Tasks[0].ExternalPredecessors.Single().ProjectPath);
        var xml = MspdiFile.Read(MspdiFile.Write(p)).Tasks[0].ExternalPredecessors.Single();
        Assert.Equal((Path.GetFullPath(supplierPath), delivery.Id), (xml.ProjectPath, xml.TaskId));
        Assert.Empty(MspdiFile.Read(MspdiFile.Write(p)).Tasks[0].Predecessors);

        // A missing file keeps the last dates and is reported.
        File.Delete(supplierPath);
        Assert.Single(ExternalLinks.Refresh(p));
        Assert.NotNull(p.Tasks[0].ExternalPredecessors[0].Finish);
    }

    [Fact]
    public void PoolOverallocationCountsOtherSharingProjects()
    {
        var pool = new Project("Pool", D(5));
        pool.AddResource("Alice");
        var poolPath = NewFile();
        ProjectFile.Save(pool, poolPath);

        var first = new Project("First", D(5));
        var firstPath = NewFile();
        ResourcePool.Share(first, poolPath, firstPath);
        first.Assign(first.AddTask("Design", 2 * Day), first.Resources[0]);
        ProjectFile.Save(first, firstPath);

        var second = new Project("Second", D(5));
        var secondPath = NewFile();
        ResourcePool.Share(second, poolPath, secondPath);
        second.Assign(second.AddTask("Review", 3 * Day), second.Resources[0]);

        Assert.Equal(new[] { Path.GetFullPath(firstPath), Path.GetFullPath(secondPath) }, ResourcePool.Sharers(poolPath));
        // Within one project Alice is fine; across both she is booked twice on Mon and Tue.
        Assert.Empty(ResourceAnalysis.FindOverallocations(second, OverallocationBasis.DayByDay));
        var over = ResourcePool.SharedOverallocations(second, secondPath).Single();
        Assert.Equal(("Alice", D(5, 0), D(7, 0)), (over.Resource, over.From, over.To));
        Assert.Equal(200, over.PeakPercent, 3);
        Assert.Equal(new[] { "Second", "First" }, over.Projects);
    }
}
