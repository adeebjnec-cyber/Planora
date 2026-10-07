using System.Globalization;
using Planora.Engine.Interop;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

/// <summary>
/// Plans saved by Microsoft Project itself (.mpp). Each file stores the dates Microsoft Project
/// calculated; Planora reschedules the plan with its own engine and must arrive at the same dates.
/// </summary>
public class RealMicrosoftProjectFileTests
{
    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    public static IEnumerable<object[]> Files() => new[]
    {
        new object[] { "Advanced Analytics Microsoft Project Plan.mpp", 34 },
        new object[] { "commercial-construction-sample.mpp", 144 },
        new object[] { "product-development-sample.mpp", 10 },
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void PlanoraSchedulesTheSameDatesAsMicrosoftProject(string file, int taskCount)
    {
        // The dates Microsoft Project stored, read straight from the .mpp by MPXJ.
        var mpp = new org.mpxj.reader.UniversalProjectReader().read(new java.io.File(Data(file)));
        var stored = mpp.getTasks().toArray().OfType<org.mpxj.Task>()
            .Where(t => t.getUniqueID() is not null && t.getStart() is not null && t.getFinish() is not null)
            .ToDictionary(t => t.getUniqueID().intValue(), t => (Start: Parse(t.getStart()), Finish: Parse(t.getFinish())));

        // Planora's own schedule of the same plan.
        var project = ForeignFormats.Load(Data(file));
        ProjectScheduler.Schedule(project);

        Assert.Equal(taskCount, project.Tasks.Count);
        var wrong = project.Tasks
            .Where(t => Math.Abs((stored[t.Id].Start - t.Start).TotalMinutes) >= 2 || Math.Abs((stored[t.Id].Finish - t.Finish).TotalMinutes) >= 2)
            .Select(t => $"{t.Name}: MS Project {stored[t.Id].Start:g} - {stored[t.Id].Finish:g}, Planora {t.Start:g} - {t.Finish:g}")
            .ToList();
        Assert.Empty(wrong);
    }

    /// <summary>Microsoft Project keeps seconds; Planora schedules in whole minutes, so dates agree within a minute.</summary>
    private static DateTime Parse(java.time.LocalDateTime value) => DateTime.Parse(value.toString(), CultureInfo.InvariantCulture);
}
