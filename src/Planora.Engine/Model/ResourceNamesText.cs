using System.Globalization;
using System.Text.RegularExpressions;

namespace Planora.Engine.Model;

/// <summary>
/// The "Resource Names" column text, as in Microsoft Project: comma-separated names with optional units
/// in brackets, e.g. "Alice, Bob[50%], Concrete[12]". Units are percentages for work resources and
/// quantities for material resources; 100% is implied when omitted.
/// </summary>
public static partial class ResourceNamesText
{
    [GeneratedRegex(@"^(?<name>[^\[\]]+?)\s*(\[\s*(?<amount>\d+(?:[.,]\d+)?)\s*(?<percent>%)?\s*[^\]]*\])?$")]
    private static partial Regex EntryPattern();

    public static string Format(ProjectTask task) =>
        string.Join(", ", task.Assignments.Select(a => a.Resource.Type switch
        {
            ResourceType.Work when Math.Abs(a.Units - 1.0) < 1e-9 => a.Resource.Name,
            ResourceType.Work => $"{a.Resource.Name}[{a.Units * 100:0.##}%]",
            ResourceType.Material => $"{a.Resource.Name}[{a.Units:0.##}{(a.Resource.MaterialLabel.Length > 0 ? " " + a.Resource.MaterialLabel : "")}]",
            _ => a.Resource.Name,
        }));

    /// <summary>
    /// Makes the task's assignments match the text: unknown names become new work resources,
    /// names no longer listed are unassigned, and changed units are updated.
    /// </summary>
    public static void Apply(Project project, ProjectTask task, string? text)
    {
        var wanted = new List<(string Name, double? Amount)>();
        foreach (var entry in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = EntryPattern().Match(entry);
            if (!match.Success)
                throw new FormatException($"'{entry}' is not a valid resource entry. Use a name with optional units, e.g. Alice[50%].");
            double? amount = match.Groups["amount"].Success
                ? double.Parse(match.Groups["amount"].Value.Replace(',', '.'), CultureInfo.InvariantCulture)
                : null;
            wanted.Add((match.Groups["name"].Value.Trim(), amount));
        }

        if (wanted.Select(w => w.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != wanted.Count)
            throw new FormatException("A resource can only be listed once.");

        foreach (var a in task.Assignments.ToList())
        {
            if (!wanted.Any(w => string.Equals(w.Name, a.Resource.Name, StringComparison.OrdinalIgnoreCase)))
                project.Unassign(task, a.Resource);
        }

        foreach (var (name, amount) in wanted)
        {
            var resource = project.Resources.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? project.AddResource(name);
            // Work resources take a percentage ("50" and "50%" mean the same); material takes a quantity.
            double units = resource.Type == ResourceType.Work
                ? (amount ?? 100) / 100.0
                : amount ?? 1;

            var existing = task.Assignments.FirstOrDefault(a => a.Resource == resource);
            if (existing is null)
                project.Assign(task, resource, units);
            else if (Math.Abs(existing.Units - units) > 1e-9 && resource.Type != ResourceType.Cost)
                project.SetUnits(existing, units);
        }
    }
}
