using Planora.Engine.Fields;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;

namespace Planora.Engine.Tests;

public class ExtendedAttributeTests
{
    private static Project Sample()
    {
        var p = new Project("Test", new DateTime(2026, 1, 5, 8, 0, 0));
        var t = p.AddTask("A", 480);
        p.CustomFields.Add(new CustomField("Text1", "Department"));
        p.CustomFields.Add(new CustomField("OutlineCode2", "Phase code") { ValueList = { "1", "1.1", "2" } });
        t.CustomValues["Text1"] = "Facilities";
        t.CustomValues["Text12"] = "Twelve";
        t.CustomValues["Number3"] = 42.5;
        t.CustomValues["Cost4"] = 1250.75m;
        t.CustomValues["Flag11"] = true;
        t.CustomValues["Date2"] = new DateTime(2026, 2, 3, 9, 0, 0);
        t.CustomValues["Duration5"] = 960L;
        t.CustomValues["OutlineCode2"] = "1.1";
        return p;
    }

    [Fact]
    public void CustomFieldsRoundTripThroughMicrosoftProjectXml()
    {
        var loaded = MspdiFile.Read(MspdiFile.Write(Sample()));
        var t = loaded.Tasks[0];
        Assert.Equal("Facilities", t.CustomValues["Text1"]);
        Assert.Equal("Twelve", t.CustomValues["Text12"]);
        Assert.Equal(42.5, t.CustomValues["Number3"]);
        Assert.Equal(1250.75m, t.CustomValues["Cost4"]);
        Assert.Equal(true, t.CustomValues["Flag11"]);
        Assert.Equal(new DateTime(2026, 2, 3, 9, 0, 0), t.CustomValues["Date2"]);
        Assert.Equal(960L, t.CustomValues["Duration5"]);
        Assert.Equal("1.1", t.CustomValues["OutlineCode2"]);
        Assert.Equal("Department", loaded.CustomFields.Single(c => c.Key == "Text1").Alias);
    }

    [Fact]
    public void MpxjReadsTheValuesAsMicrosoftProjectWould()
    {
        var path = Path.Combine(Path.GetTempPath(), $"planora-ext-{Guid.NewGuid():N}.xml");
        try
        {
            MspdiFile.Save(Sample(), path);
            var file = new org.mpxj.mspdi.MSPDIReader().read(path);
            var task = file.getTasks().toArray().OfType<org.mpxj.Task>().First(x => x.getName() == "A");
            Assert.Equal("Facilities", task.getText(1));
            Assert.Equal("Twelve", task.getText(12));
            Assert.Equal(42.5, ((java.lang.Number)task.getNumber(3)).doubleValue(), 6);
            Assert.Equal(1250.75, ((java.lang.Number)task.getCost(4)).doubleValue(), 6);
            Assert.True(task.getFlag(11));
            Assert.Equal(2.0, task.getDuration(5).getDuration(), 6);   // 16 hours shown as 2 days
            Assert.Equal(org.mpxj.TimeUnit.DAYS, task.getDuration(5).getUnits());
            Assert.Equal("1.1", task.getOutlineCode(2));
            Assert.Equal("Department", file.getCustomFields().get(org.mpxj.TaskField.TEXT1).getAlias());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IndicatorsAndOutlineCodesAreSaved()
    {
        var p = Sample();
        p.CustomFields.Add(new CustomField("Number3") { Indicators = { IndicatorRule.Parse(">= 40 ▲"), IndicatorRule.Parse("else ○") } });
        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        var field = loaded.CustomFields.Single(c => c.Key == "Number3");
        Assert.Equal("▲", field.IndicatorFor(loaded, loaded.Tasks[0]));
        Assert.True(loaded.CustomFields.Single(c => c.Key == "OutlineCode2").IsOutlineCode);
        Assert.Contains("OutlineCode2", CustomField.AllSlots());
        Assert.Equal(">= 40 ▲", field.Indicators[0].ToString());
    }
}
