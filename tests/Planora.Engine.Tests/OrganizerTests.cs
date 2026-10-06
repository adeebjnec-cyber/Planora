using Planora.Engine.Calendars;
using Planora.Engine.Fields;
using Planora.Engine.Interop;
using Planora.Engine.Macros;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.Engine.Tests;

public class OrganizerTests
{
    [Fact]
    public void CopiesDefinitionsWithoutSharingThem()
    {
        var from = new Project("From", new DateTime(2026, 1, 5, 8, 0, 0));
        var night = WorkingCalendar.CreateNightShift("Nights");
        from.Calendars.Add(night);
        from.Filters.Add(new FilterDefinition("Long", new FilterCondition("Duration", FilterOperator.GreaterThan, "5d")));
        from.CustomFields.Add(new CustomField("Text3", "Area"));
        from.Macros.Add(new MacroModule { Name = "Tools", Code = "MsgBox 1" });

        var to = new Project("To", new DateTime(2026, 1, 5, 8, 0, 0));
        Organizer.Copy(from, to, OrganizerItemKind.Calendar, "Nights");
        Organizer.Copy(from, to, OrganizerItemKind.Filter, "Long");
        Organizer.Copy(from, to, OrganizerItemKind.CustomField, "Text3 (Area)");
        Organizer.Copy(from, to, OrganizerItemKind.Macro, "Tools");

        Assert.Contains("Nights", Organizer.Names(to, OrganizerItemKind.Calendar));
        Assert.NotSame(night, to.Calendars.Single(c => c.Name == "Nights"));
        Assert.Equal("Long", to.Filters.Single().Name);
        Assert.Equal("Area", to.CustomFields.Single().Alias);
        Assert.Equal("MsgBox 1", to.Macros.Single().Code);

        // Copying again replaces rather than duplicates; deleting removes.
        Organizer.Copy(from, to, OrganizerItemKind.Filter, "Long");
        Assert.Single(to.Filters);
        Assert.True(Organizer.Delete(to, OrganizerItemKind.Filter, "Long"));
        Assert.False(Organizer.Delete(to, OrganizerItemKind.Calendar, to.Calendar.Name));   // in use
    }
}
