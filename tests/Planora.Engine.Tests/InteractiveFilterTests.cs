using Planora.Engine.Model;
using Planora.Engine.Scheduling;
using Planora.Engine.Views;

namespace Planora.Engine.Tests;

public class InteractiveFilterTests
{
    [Fact]
    public void PromptedValuesAreFilledInBeforeFiltering()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var bob = p.AddResource("Bob");
        var a = p.AddTask("A", 480);
        var b = p.AddTask("B", 480);
        p.Assign(a, bob);
        ProjectScheduler.Schedule(p);

        var filter = FilterDefinition.Builtin.First(f => f.Name == "Using Resource...");
        Assert.True(filter.IsInteractive);
        Assert.Equal(new[] { "Show tasks using resource:" }, filter.Prompts);
        Assert.Empty(filter.Apply(p, p.Tasks));     // unanswered prompts match nothing

        var answered = filter.WithAnswers(new Dictionary<string, string> { ["Show tasks using resource:"] = "bob" });
        Assert.False(answered.IsInteractive);
        Assert.Equal(new[] { a }, answered.Apply(p, p.Tasks));

        var range = FilterDefinition.Builtin.First(f => f.Name == "Task Range...")
            .WithAnswers(new Dictionary<string, string> { ["Show tasks with IDs from:"] = "2", ["To:"] = "2" });
        Assert.Equal(new[] { b }, range.Apply(p, p.Tasks));
    }
}
