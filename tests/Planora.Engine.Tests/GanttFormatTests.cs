using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Views;

namespace Planora.Engine.Tests;

public class GanttFormatTests
{
    [Fact]
    public void FlagStylesOverrideTheKindsStyleOnlyForFlaggedTasks()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var a = p.AddTask("A", 480);
        var b = p.AddTask("B", 480);
        b.IsMarked = true;
        p.GanttFormat.BarStyles.Add(new BarStyle { Name = "Marked", Kind = BarKind.Task, FlagField = "Marked", Fill = "#FFE680" });

        Assert.Equal("#8AB4E6", p.GanttFormat.StyleFor(p, a, BarKind.Task).Fill);
        Assert.Equal("#FFE680", p.GanttFormat.StyleFor(p, b, BarKind.Task).Fill);
        Assert.Equal("#FFE680", p.GanttFormat.StyleFor(p, b, BarKind.CriticalTask).Fill);
    }

    [Fact]
    public void TierLabelsSupportQuartersAndHalves()
    {
        var date = new DateTime(2026, 8, 3);
        Assert.Equal("Q3 2026", new TimescaleTierSetting { Label = "'Q'{Q} yyyy" }.Format(date));
        Assert.Equal("H2", new TimescaleTierSetting { Label = "'H'{H}" }.Format(date));
        Assert.Equal("Aug", new TimescaleTierSetting { Label = "MMM" }.Format(date));
    }

    [Fact]
    public void FormatIsSavedWithTheProject()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        p.GanttFormat.BarStyles[0].Fill = "#123456";
        p.GanttFormat.Timescale.Automatic = false;
        p.GanttFormat.Timescale.TierCount = 3;
        p.GanttFormat.Gridlines.RowLines = true;
        p.GanttFormat.TextStyles.First(t => t.Item == TextItem.CriticalTasks).Color = "#C04030";

        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p)).GanttFormat;
        Assert.Equal("#123456", loaded.BarStyles[0].Fill);
        Assert.False(loaded.Timescale.Automatic);
        Assert.Equal(3, loaded.Timescale.TierCount);
        Assert.True(loaded.Gridlines.RowLines);
        Assert.Equal("#C04030", loaded.Text(TextItem.CriticalTasks).Color);
    }

    [Fact]
    public void PageSetupCodesAndSaving()
    {
        var p = new Project("Plan", new DateTime(2026, 1, 5, 8, 0, 0)) { StatusDate = new DateTime(2026, 1, 9, 17, 0, 0) };
        p.AddTask("A", 480);
        Assert.Equal("Plan: page 2 of 5 (Gantt Chart)", PageSetup.Expand("&[Project Title]: page &[Page] of &[Pages] (&[View])", p, 2, 5, "Gantt Chart"));
        Assert.Contains("2026", PageSetup.Expand("&[Status Date]", p, 1, 1, ""));

        p.PageSetup.Landscape = false;
        p.PageSetup.Paper = PaperSize.Letter;
        p.PageSetup.Legend = LegendPlacement.EveryPage;
        var (w, h) = p.PageSetup.PageSizeDips();
        Assert.Equal(816, w, 0);       // 8.5 in at 96 per inch
        Assert.Equal(1056, h, 0);
        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p)).PageSetup;
        Assert.Equal((PaperSize.Letter, false, LegendPlacement.EveryPage), (loaded.Paper, loaded.Landscape, loaded.Legend));
    }
}
