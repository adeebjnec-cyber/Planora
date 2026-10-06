using Planora.Engine.Model;

namespace Planora.Engine.Tests;

public class PredecessorTextTests
{
    private static (Project, ProjectTask[]) ThreeTasks()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        return (p, new[] { p.AddTask("A", 480), p.AddTask("B", 480), p.AddTask("C", 480) });
    }

    [Fact]
    public void ParsesAndFormats()
    {
        var (p, t) = ThreeTasks();

        PredecessorText.Apply(p, t[2], "1, 2SS+2d");

        Assert.Equal(2, t[2].Predecessors.Count);
        Assert.Equal(LinkType.StartToStart, t[2].Predecessors[1].Type);
        Assert.Equal(960, t[2].Predecessors[1].LagMinutes);
        Assert.Equal("1,2SS+2 days", PredecessorText.Format(p, t[2]));
    }

    [Fact]
    public void ParsesLeadsAndPercentLags()
    {
        var (p, t) = ThreeTasks();

        PredecessorText.Apply(p, t[2], "1FF-1d;2fs+50%");

        Assert.Equal(-480, t[2].Predecessors[0].LagMinutes);
        Assert.Equal(LagFormat.PercentOfPredecessor, t[2].Predecessors[1].LagFormat);
        Assert.Equal("1FF-1 day,2FS+50%", PredecessorText.Format(p, t[2]));
    }

    [Fact]
    public void EmptyTextRemovesLinks()
    {
        var (p, t) = ThreeTasks();
        p.Link(t[0], t[1]);
        PredecessorText.Apply(p, t[1], " ");
        Assert.Empty(t[1].Predecessors);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("1XX")]
    [InlineData("1+abc")]
    public void InvalidTextKeepsExistingLinks(string text)
    {
        var (p, t) = ThreeTasks();
        p.Link(t[0], t[2]);
        Assert.Throws<FormatException>(() => PredecessorText.Apply(p, t[2], text));
        Assert.Single(t[2].Predecessors);
    }

    [Fact]
    public void InvalidLinkKeepsExistingLinks()
    {
        var (p, t) = ThreeTasks();
        p.Link(t[0], t[2]);
        Assert.Throws<InvalidOperationException>(() => PredecessorText.Apply(p, t[2], "3"));
        Assert.Single(t[2].Predecessors);
    }
}
