using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Planora.Engine.Fields;
using Planora.Engine.Model;

namespace Planora.Engine.Interop;

/// <summary>
/// Custom task fields in Microsoft Project XML: the project's ExtendedAttribute definitions (alias,
/// formula, value list) and each task's ExtendedAttribute values. Field IDs are Microsoft Project's
/// (as used by MPXJ), e.g. Text1 = 188743731.
/// </summary>
internal static class MspdiExtendedAttributes
{
    private static readonly Dictionary<string, int> Ids = BuildIds();
    private static readonly Dictionary<int, string> Keys = Ids.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static Dictionary<string, int> BuildIds()
    {
        var ids = new Dictionary<string, int>();
        void Range(string prefix, int first, int count, int startId, int step = 1)
        {
            for (int i = 0; i < count; i++)
                ids[$"{prefix}{first + i}"] = startId + i * step;
        }
        Range("Text", 1, 3, 188743731, 3);
        Range("Text", 4, 3, 188743740, 3);
        Range("Text", 7, 4, 188743747);
        Range("Text", 11, 20, 188743997);
        Range("Number", 1, 5, 188743767);
        Range("Number", 6, 15, 188743982);
        Range("Date", 1, 10, 188743945);
        Range("Flag", 1, 10, 188743752);
        Range("Flag", 11, 10, 188743972);
        Range("Cost", 1, 3, 188743786);
        Range("Cost", 4, 7, 188743938);
        Range("Duration", 1, 3, 188743783);
        Range("Duration", 4, 7, 188743955);
        Range("OutlineCode", 1, 10, 188744096, 2);
        return ids;
    }

    public static int? FieldId(string key) => Ids.TryGetValue(key, out var id) ? id : null;

    public static string? KeyOf(int fieldId) => Keys.TryGetValue(fieldId, out var key) ? key : null;

    /// <summary>The project's ExtendedAttributes element: every custom field that has a definition or a value.</summary>
    public static XElement? WriteDefinitions(XNamespace ns, Project project)
    {
        var keys = project.CustomFields.Select(c => c.Key)
            .Concat(project.Tasks.SelectMany(t => t.CustomValues.Keys))
            .Where(k => Ids.ContainsKey(k))
            .Distinct()
            .OrderBy(k => Ids[k])
            .ToList();
        if (keys.Count == 0)
            return null;
        return new XElement(ns + "ExtendedAttributes", keys.Select(key =>
        {
            var definition = project.CustomFields.FirstOrDefault(c => c.Key == key);
            var element = new XElement(ns + "ExtendedAttribute",
                new XElement(ns + "FieldID", Ids[key].ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "FieldName", key));
            if (definition is { Alias.Length: > 0 })
                element.Add(new XElement(ns + "Alias", definition.Alias));
            if (definition?.Formula is { Length: > 0 } formula)
                element.Add(new XElement(ns + "Formula", formula));
            return element;
        }));
    }

    /// <summary>A task's custom values as ExtendedAttribute elements.</summary>
    public static IEnumerable<XElement> WriteValues(XNamespace ns, ProjectTask task)
    {
        foreach (var (key, value) in task.CustomValues.OrderBy(kv => Ids.TryGetValue(kv.Key, out var id) ? id : int.MaxValue))
        {
            if (!Ids.TryGetValue(key, out var fieldId) || value is null || CustomField.TypeOfKey(key) is not { } type)
                continue;
            var element = new XElement(ns + "ExtendedAttribute",
                new XElement(ns + "FieldID", fieldId.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "Value", Format(value, type)));
            if (type == FieldType.Duration)
                element.Add(new XElement(ns + "DurationFormat", 7));
            yield return element;
        }
    }

    private static string Format(object value, FieldType type) => (value, type) switch
    {
        (bool b, _) => b ? "1" : "0",
        (DateTime d, _) => d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        // Microsoft Project stores custom costs in hundredths.
        (decimal m, _) => (m * 100).ToString("0.##", CultureInfo.InvariantCulture),
        (long minutes, FieldType.Duration) => XmlConvert.ToString(TimeSpan.FromMinutes(minutes)),
        (double n, _) => n.ToString("0.############", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Reads the definitions into the project's custom fields (alias and formula).</summary>
    public static void ReadDefinitions(XElement root, Project project)
    {
        foreach (var attribute in Elements(root, "ExtendedAttributes", "ExtendedAttribute"))
        {
            if (!int.TryParse(Child(attribute, "FieldID"), out var id) || KeyOf(id) is not { } key)
                continue;
            string alias = Child(attribute, "Alias") ?? "";
            string? formula = Child(attribute, "Formula");
            if (alias.Length == 0 && string.IsNullOrWhiteSpace(formula))
                continue;
            project.CustomFields.RemoveAll(c => c.Key == key);
            var field = new CustomField(key, alias);
            if (!string.IsNullOrWhiteSpace(formula))
            {
                // Keep Microsoft Project formulas only if Planora's formula language understands them.
                try
                {
                    Formula.Parse(formula);
                    field.Formula = formula;
                }
                catch (FormulaException)
                {
                }
            }
            project.CustomFields.Add(field);
        }
    }

    /// <summary>Reads a task's ExtendedAttribute values.</summary>
    public static void ReadValues(XElement taskElement, ProjectTask task)
    {
        foreach (var attribute in taskElement.Elements().Where(e => e.Name.LocalName == "ExtendedAttribute"))
        {
            if (!int.TryParse(Child(attribute, "FieldID"), out var id) || KeyOf(id) is not { } key
                || CustomField.TypeOfKey(key) is not { } type || Child(attribute, "Value") is not { } text)
                continue;
            if (Parse(text, type) is { } value)
                task.CustomValues[key] = value;
        }
    }

    private static object? Parse(string text, FieldType type)
    {
        try
        {
            return type switch
            {
                FieldType.Text => text,
                FieldType.Flag => text is "1" or "true",
                FieldType.Date => DateTime.Parse(text, CultureInfo.InvariantCulture),
                FieldType.Cost => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) / 100m,
                FieldType.Duration => (long)Math.Round(XmlConvert.ToTimeSpan(text).TotalMinutes),
                _ => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IEnumerable<XElement> Elements(XElement root, string container, string item) =>
        root.Elements().Where(e => e.Name.LocalName == container).SelectMany(c => c.Elements().Where(e => e.Name.LocalName == item));

    private static string? Child(XElement parent, string name) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
}
