using Planora.Engine.Macros;
using Planora.Engine.Model;
using Planora.Engine.Persistence;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class MacroTests
{
    private const int Day = 480;

    private static Project Sample()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var phase = p.AddTask("Phase");
        var a = p.AddTask("Short", Day);
        var b = p.AddTask("Long", 6 * Day);
        p.Indent(a);
        p.Indent(b);
        p.Link(a, b);
        p.AddResource("Bob");
        ProjectScheduler.Schedule(p);
        return p;
    }

    [Fact]
    public void ForEachIfAndFieldAssignments()
    {
        var p = Sample();
        var result = Macro.Run(p, """
            Sub MarkLongTasks()
                Dim count
                count = 0
                For Each t In ActiveProject.Tasks
                    If Not t.Summary And t.Duration > 5 * 480 Then
                        t.Marked = True
                        t.Text1 = "Long: " & t.Name
                        count = count + 1
                    ElseIf t.Summary Then
                        t.[Text2] = "summary"
                    Else
                        [Number1] = t.Duration / 480
                    End If
                Next t
                MsgBox count & " long task(s)"
            End Sub
            """);
        Assert.Equal(new[] { "1 long task(s)" }, result.Messages);
        Assert.True(p.Tasks[2].IsMarked);
        Assert.Equal("Long: Long", p.Tasks[2].CustomValues["Text1"]);
        Assert.Equal("summary", p.Tasks[0].CustomValues["Text2"]);
        Assert.Equal(1.0, p.Tasks[1].CustomValues["Number1"]);
    }

    [Fact]
    public void ChangesRescheduleBeforeTheyAreRead()
    {
        var p = Sample();
        var result = Macro.Run(p, """
            Set t = Tasks.Add("Added")
            t.Duration = "2d"
            For Each x In Tasks
                If x.Name = "Long" Then LinkTasks x, t
            Next
            Debug.Print t.Name, t.Start
            For i = 1 To 3
                AssignResource t, "Bob", 0.5
                Exit For
            Next i
            Do While t.Priority < 600
                t.Priority = t.Priority + 50
            Loop
            """);
        var added = p.Tasks.Single(x => x.Name == "Added");
        var longTask = p.Tasks.Single(x => x.Name == "Long");
        Assert.Equal(2 * Day, added.DurationMinutes);
        Assert.Contains(added.Predecessors, l => l.PredecessorId == longTask.Id);
        Assert.True(added.Start >= longTask.Finish);
        // Debug.Print saw the rescheduled start, after the link.
        Assert.Contains(added.Start.ToString("g", System.Globalization.CultureInfo.CurrentCulture), result.Output.Single());
        Assert.Single(added.Assignments);
        Assert.Equal(600, added.Priority);
    }

    [Fact]
    public void SubsCanCallEachOtherAndErrorsGiveTheLine()
    {
        var p = Sample();
        var code = """
            Sub Main()
                Call Helper
                MsgBox "back"
            End Sub

            Sub Helper()
                MsgBox "helper"
                Exit Sub
                MsgBox "never"
            End Sub
            """;
        Assert.Equal(new[] { "Main", "Helper" }, Macro.SubNames(code));
        Assert.Equal(new[] { "helper", "back" }, Macro.Run(p, code, "Main").Messages);

        var error = Assert.Throws<MacroException>(() => Macro.Run(p, "x = 1\ny = Nosuch.Thing\n"));
        Assert.Equal(2, error.Line);
        Assert.Throws<MacroException>(() => Macro.Run(p, "Do\nLoop\n"));   // runs too long
    }

    [Fact]
    public void ResourcesAndSelectionAndSaving()
    {
        var p = Sample();
        Macro.Run(p, """
            For Each r In Resources
                r.StandardRate = 25
                r.Group = "Team " & r.Name
            Next
            For Each t In SelectedTasks
                t.Notes = "picked"
            Next
            """, selection: new[] { p.Tasks[1] });
        Assert.Equal(25m, p.Resources[0].StandardRate);
        Assert.Equal("Team Bob", p.Resources[0].Group);
        Assert.Equal("picked", p.Tasks[1].Notes);
        Assert.Equal("", p.Tasks[2].Notes);

        p.Macros.Add(new MacroModule { Name = "Module1", Code = "MsgBox 1" });
        Assert.Equal("MsgBox 1", ProjectFile.Deserialize(ProjectFile.Serialize(p)).Macros.Single().Code);
    }
}
