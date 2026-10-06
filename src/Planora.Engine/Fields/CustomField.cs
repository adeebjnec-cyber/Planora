namespace Planora.Engine.Fields;

public enum FieldType
{
    Text,
    Number,
    Date,
    Flag,
    Cost,
    Duration,
    Percent,
    Work,
}

/// <summary>
/// A user-defined task field, as in Microsoft Project: one of the fixed slots (Text1-30, Number1-20,
/// Date1-10, Flag1-20, Cost1-10, Duration1-10) given an alias and, optionally, a list of allowed values.
/// </summary>
public sealed class CustomField
{
    public static readonly IReadOnlyDictionary<FieldType, int> SlotCounts = new Dictionary<FieldType, int>
    {
        [FieldType.Text] = 30,
        [FieldType.Number] = 20,
        [FieldType.Date] = 10,
        [FieldType.Flag] = 20,
        [FieldType.Cost] = 10,
        [FieldType.Duration] = 10,
    };

    public CustomField(string key, string alias = "")
    {
        Type = TypeOfKey(key) ?? throw new ArgumentException($"'{key}' is not a custom field slot (e.g. Text1, Number3, Flag2).");
        Key = key;
        Alias = alias;
    }

    /// <summary>The slot, e.g. "Text1".</summary>
    public string Key { get; }

    public FieldType Type { get; }

    /// <summary>Display name chosen by the user, e.g. "Department". Empty to use the slot name.</summary>
    public string Alias { get; set; }

    /// <summary>Allowed values (a pick list). Empty means any value.</summary>
    public List<string> ValueList { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Key : Alias;

    /// <summary>All slot keys, e.g. Text1..Text30, Number1..Number20.</summary>
    public static IEnumerable<string> AllSlots() =>
        SlotCounts.SelectMany(kv => Enumerable.Range(1, kv.Value).Select(i => $"{kv.Key}{i}"));

    public static FieldType? TypeOfKey(string key)
    {
        foreach (var (type, count) in SlotCounts)
        {
            var prefix = type.ToString();
            if (key.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(key[prefix.Length..], out int n) && n >= 1 && n <= count)
                return type;
        }
        return null;
    }
}
