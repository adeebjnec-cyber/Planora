using Planora.Engine.Model;

namespace Planora.Engine.Tests;

public class ResourceNamesTextTests
{
    private static (Project, ProjectTask) OneTask()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        return (p, p.AddTask("A", 960));
    }

    [Fact]
    public void CreatesResourcesAndAssigns()
    {
        var (p, task) = OneTask();

        ResourceNamesText.Apply(p, task, "Alice, Bob[50%]");

        Assert.Equal(new[] { "Alice", "Bob" }, p.Resources.Select(r => r.Name));
        Assert.Equal(0.5, task.Assignments[1].Units, 3);
        Assert.Equal("Alice, Bob[50%]", ResourceNamesText.Format(task));
    }

    [Fact]
    public void MatchesExistingResourcesAndUpdatesUnits()
    {
        var (p, task) = OneTask();
        var alice = p.AddResource("Alice");
        p.Assign(task, alice);

        ResourceNamesText.Apply(p, task, "alice[25%]");

        Assert.Single(p.Resources);
        Assert.Equal(0.25, task.Assignments.Single().Units, 3);
    }

    [Fact]
    public void RemovesUnlistedAssignments()
    {
        var (p, task) = OneTask();
        ResourceNamesText.Apply(p, task, "Alice, Bob");
        ResourceNamesText.Apply(p, task, "Bob");
        Assert.Equal("Bob", ResourceNamesText.Format(task));
        Assert.Equal(2, p.Resources.Count); // resources stay in the pool
    }

    [Fact]
    public void MaterialQuantities()
    {
        var (p, task) = OneTask();
        var concrete = p.AddResource("Concrete", ResourceType.Material);
        concrete.MaterialLabel = "m3";

        ResourceNamesText.Apply(p, task, "Concrete[12 m3]");

        Assert.Equal(12, task.Assignments.Single().Units);
        Assert.Equal("Concrete[12 m3]", ResourceNamesText.Format(task));
    }

    [Theory]
    [InlineData("Alice, alice")]
    [InlineData("Bad]name")]
    public void RejectsInvalidText(string text)
    {
        var (p, task) = OneTask();
        Assert.Throws<FormatException>(() => ResourceNamesText.Apply(p, task, text));
    }
}
