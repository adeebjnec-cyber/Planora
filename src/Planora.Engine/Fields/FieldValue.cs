using System.Globalization;
using Planora.Engine.Model;

namespace Planora.Engine.Fields;

/// <summary>
/// Conversions between field values and text: display formatting, parsing typed input, invariant
/// storage (for custom field values in files), and comparison (for filters and sorting).
/// Values are: string (Text), double (Number, Percent), DateTime (Date), bool (Flag), decimal (Cost),
/// long minutes (Duration, Work). Null means "no value" (shown as NA for dates, empty otherwise).
/// </summary>
public static class FieldValue
{
    public static string Format(object? value, FieldType type, ScheduleOptions options) => value switch
    {
        null => type == FieldType.Date ? "NA" : "",
        bool b => b ? "Yes" : "No",
        DateTime d => d.ToString("ddd ", CultureInfo.CurrentCulture) + d.ToString("d", CultureInfo.CurrentCulture),
        decimal m => m.ToString("C", CultureInfo.CurrentCulture),
        long minutes when type == FieldType.Work => DurationText.Format(minutes, options, DurationUnit.Hours),
        long minutes => DurationText.Format(minutes, options),
        double n when type == FieldType.Percent => n.ToString("0.#", CultureInfo.CurrentCulture) + "%",
        double n => n.ToString("0.##", CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Parses user input for a field of the given type. Empty input gives null.</summary>
    public static bool TryParse(string? text, FieldType type, ScheduleOptions options, out object? value)
    {
        value = null;
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0 || type == FieldType.Date && trimmed.Equals("NA", StringComparison.OrdinalIgnoreCase))
            return true;

        switch (type)
        {
            case FieldType.Text:
                value = trimmed;
                return true;
            case FieldType.Number:
            case FieldType.Percent:
                if (double.TryParse(trimmed.TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ||
                    double.TryParse(trimmed.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    value = number;
                    return true;
                }
                return false;
            case FieldType.Cost:
                if (decimal.TryParse(trimmed, NumberStyles.Currency, CultureInfo.CurrentCulture, out var money) ||
                    decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out money))
                {
                    value = money;
                    return true;
                }
                return false;
            case FieldType.Flag:
                var lower = trimmed.ToLowerInvariant();
                if (lower is "yes" or "y" or "true" or "1")
                    value = true;
                else if (lower is "no" or "n" or "false" or "0")
                    value = false;
                else
                    return false;
                return true;
            case FieldType.Date:
                // Accept the display format, which starts with a day name.
                int space = trimmed.IndexOf(' ');
                if (space > 0 && !char.IsDigit(trimmed[0]))
                    trimmed = trimmed[(space + 1)..];
                if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
                {
                    value = date;
                    return true;
                }
                return false;
            default: // Duration, Work
                if (DurationText.TryParse(trimmed, options, out var d, type == FieldType.Work ? DurationUnit.Hours : DurationUnit.Days))
                {
                    value = d.Minutes;
                    return true;
                }
                return false;
        }
    }

    /// <summary>Culture-independent text for storing a value in a file.</summary>
    public static string? ToStorage(object? value) => value switch
    {
        null => null,
        bool b => b ? "1" : "0",
        DateTime d => d.ToString("s", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    public static object? FromStorage(string? text, FieldType type)
    {
        if (text is null)
            return null;
        return type switch
        {
            FieldType.Text => text,
            FieldType.Number or FieldType.Percent => double.Parse(text, CultureInfo.InvariantCulture),
            FieldType.Cost => decimal.Parse(text, CultureInfo.InvariantCulture),
            FieldType.Flag => text == "1",
            FieldType.Date => DateTime.Parse(text, CultureInfo.InvariantCulture),
            _ => long.Parse(text, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Orders values of one field; nulls sort first.</summary>
    public static int Compare(object? a, object? b)
    {
        if (a is null || b is null)
            return a is null ? (b is null ? 0 : -1) : 1;
        return (a, b) switch
        {
            (string x, string y) => string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase),
            (IComparable x, _) when a.GetType() == b.GetType() => x.CompareTo(b),
            _ => Convert.ToDouble(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(b, CultureInfo.InvariantCulture)),
        };
    }
}
