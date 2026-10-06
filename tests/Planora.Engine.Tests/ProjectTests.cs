using Planora.Engine.Model;

namespace Planora.Engine.Tests;

public class ProjectTests
{
    private static Project NewProject() => new("Test", new DateTime(2026, 1, 5, 8, 0, 0));

    [Fact]
    public void IndentAndOutdentBuildHierarchy()
    {
        var p = NewProject();
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A");
        var b = p.AddTask("B");

        p.Indent(a);
        p.Indent(b);
        Assert.Equal(new[] { a, b }, phase.Children);
        Assert.Equal(new[] { "1", "1.1", "1.2" }, p.Tasks.Select(t => t.WbsCode));

        p.Outdent(a);
        // B now sits below A at a deeper level, so it becomes A's subtask.
        Assert.Empty(phase.Children);
        Assert.Equal(a, b.Parent);
    }

    [Fact]
    public void IndentMovesSubtree()
    {
        var p = NewProject();
        p.AddTask("Top");
        var phase = p.AddTask("Phase");
        var child = p.AddTask("Child");
        p.Indent(child);
        p.Indent(phase);

        Assert.Equal(2, phase.OutlineLevel);
        Assert.Equal(3, child.OutlineLevel);
    }

    [Fact]
    public void FirstTaskCannotBeIndented()
    {
        var p = NewProject();
        var a = p.AddTask("A");
        Assert.Throws<InvalidOperationException>(() => p.Indent(a));
    }

    [Fact]
    public void InsertTakesLevelOfDisplacedRow()
    {
        var p = NewProject();
        p.AddTask("Phase");
        var a = p.AddTask("A");
        p.Indent(a);
        var inserted = p.InsertTask(1, "New");
        Assert.Equal(2, inserted.OutlineLevel);
    }

    [Fact]
    public void RemovingTaskRemovesSubtasksAndLinks()
    {
        var p = NewProject();
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A");
        p.Indent(a);
        var b = p.AddTask("B");
        p.Link(a, b);

        p.RemoveTask(phase);

        Assert.Equal(new[] { b }, p.Tasks);
        Assert.Empty(b.Predecessors);
    }

    [Fact]
    public void InvalidLinksAreRejected()
    {
        var p = NewProject();
        var phase = p.AddTask("Phase");
        var a = p.AddTask("A");
        p.Indent(a);
        var b = p.AddTask("B");

        Assert.Throws<InvalidOperationException>(() => p.Link(a, a));
        Assert.Throws<InvalidOperationException>(() => p.Link(phase, a));
        Assert.Throws<InvalidOperationException>(() => p.Link(b, phase, LinkType.FinishToFinish));
        p.Link(a, b);
        Assert.Throws<InvalidOperationException>(() => p.Link(b, a));
    }

    [Fact]
    public void IndentingRemovesLinksThatBecomeParentChild()
    {
        var p = NewProject();
        var a = p.AddTask("A");
        var b = p.AddTask("B");
        p.Link(a, b);
        p.Indent(b);
        Assert.Empty(b.Predecessors);
    }
}
