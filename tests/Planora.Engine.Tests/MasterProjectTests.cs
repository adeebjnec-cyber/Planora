using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class MasterProjectTests : IDisposable
{
    private const int Day = 480;
    private readonly List<string> _files = new();

    private static DateTime D(int day, int hour = 8) => new(2026, 1, day, hour, 0, 0);

    private string NewFile(string extension = ".planora")
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

    private string SaveSub(string name, params (string Task, int Days)[] tasks)
    {
        var p = new Project(name, D(5));
        ProjectTask? previous = null;
        foreach (var (taskName, days) in tasks)
        {
            var t = p.AddTask(taskName, days * Day);
            if (previous is not null)
                p.Link(previous, t);
            previous = t;
        }
        var path = NewFile();
        ProjectFile.Save(p, path);
        return path;
    }

    [Fact]
    public void InsertedProjectBecomesASummaryWithItsTasks()
    {
        var subPath = SaveSub("Design", ("Sketch", 2), ("Review", 1));
        var master = new Project("Programme", D(5));
        master.AddTask("Kick-off", 0);

        var summary = MasterProject.InsertSubproject(master, subPath, 1);
        ProjectScheduler.Schedule(master);

        Assert.True(summary.IsSubproject);
        Assert.Equal(new[] { "Kick-off", "Design", "Sketch", "Review" }, master.Tasks.Select(t => t.Name));
        Assert.Equal(new[] { "Sketch", "Review" }, summary.Children.Select(c => c.Name));
        Assert.Equal(D(7, 17), summary.Finish);
    }

    [Fact]
    public void CrossProjectLinksAreOrdinaryLinks()
    {
        var master = new Project("Programme", D(5));
        var a = MasterProject.InsertSubproject(master, SaveSub("A", ("A1", 2)), 0);
        var b = MasterProject.InsertSubproject(master, SaveSub("B", ("B1", 1)), master.Tasks.Count);
        master.Link(a.Children[0], b.Children[0]);
        ProjectScheduler.Schedule(master);

        Assert.Equal(D(7), b.Children[0].Start);
    }

    [Fact]
    public void SubprojectsRefreshFromAndSaveToTheirFiles()
    {
        var subPath = SaveSub("Build", ("Code", 3));
        var master = new Project("Programme", D(5));
        var summary = MasterProject.InsertSubproject(master, subPath, 0);

        // Edit in the master and save back.
        master.SetDuration(summary.Children[0], 5 * Day);
        master.InsertTask(master.IndexOf(summary.Children[0]) + 1, "Test", Day);
        ProjectScheduler.Schedule(master);
        MasterProject.SaveSubproject(master, summary);
        var saved = ProjectFile.Load(subPath);
        Assert.Equal(new[] { "Code", "Test" }, saved.Tasks.Select(t => t.Name));
        Assert.Equal(5 * Day, saved.Tasks[0].DurationMinutes);

        // Someone else edits the file; refresh picks it up.
        saved.Tasks[0].Name = "Code (v2)";
        ProjectFile.Save(saved, subPath);
        MasterProject.RefreshSubproject(master, summary);
        Assert.Equal(new[] { "Code (v2)", "Test" }, summary.Children.Select(c => c.Name));
    }

    [Fact]
    public void ResourcePoolIsSharedAndUpdated()
    {
        var pool = new Project("Pool", D(5));
        var alice = pool.AddResource("Alice");
        alice.StandardRate = 50;
        alice.Calendar!.AddHoliday("Leave", new DateOnly(2026, 1, 6));
        pool.AddResource("Bob").MaxUnits = 0.5;
        var poolPath = NewFile();
        ProjectFile.Save(pool, poolPath);

        var project = new Project("Project", D(5));
        project.AddResource("alice"); // matched by name
        Assert.Equal(1, ResourcePool.Share(project, poolPath));
        Assert.Equal(2, project.Resources.Count);
        Assert.Equal(50m, project.Resources[0].StandardRate);
        Assert.False(project.Resources[0].Calendar!.IsWorkingDay(new DateOnly(2026, 1, 6)));
        Assert.Equal(0.5, project.Resources[1].MaxUnits);

        // A rate change in the project goes back to the pool, and another project can pick it up.
        project.Resources[0].StandardRate = 55;
        project.AddResource("Carol");
        ResourcePool.Update(project);
        var other = new Project("Other", D(5));
        ResourcePool.Share(other, poolPath);
        Assert.Equal(55m, other.Resources.Single(r => r.Name.Equals("alice", StringComparison.OrdinalIgnoreCase)).StandardRate);
        Assert.Contains(other.Resources, r => r.Name == "Carol");

        var saved = ProjectFile.Deserialize(ProjectFile.Serialize(other));
        Assert.Equal(poolPath, saved.ResourcePoolPath);
    }

    [Fact]
    public void MicrosoftProjectXmlSubprojects()
    {
        var sub = new Project("Fit-out", D(5));
        sub.AddTask("Paint", 2 * Day);
        var path = NewFile(".xml");
        MspdiFile.Save(sub, path);

        var master = new Project("Programme", D(5));
        var summary = MasterProject.InsertSubproject(master, path, 0);
        Assert.Equal("Paint", summary.Children.Single().Name);
    }
}
