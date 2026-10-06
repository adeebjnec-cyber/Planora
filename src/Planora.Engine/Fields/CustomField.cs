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
/// A graphical indicator rule: when the field's value passes the test, the symbol is shown instead of the
/// value. A rule without an operator ("else") matches any value.
/// </summary>
public sealed record IndicatorRule(Views.FilterOperator? Operator, string Value, string Symbol)
{
    private static readonly (string Text, Views.FilterOperator Op)[] Operators =
    {
        ("<>", Views.FilterOperator.NotEquals), (">=", Views.FilterOperator.GreaterThanOrEqual), ("<=", Views.FilterOperator.LessThanOrEqual),
        ("=", Views.FilterOperator.Equals), (">", Views.FilterOperator.GreaterThan), ("<", Views.FilterOperator.LessThan),
        ("contains", Views.FilterOperator.Contains),
    };

    /// <summary>Parses "test value symbol", e.g. "&gt;= 100 ✓", "contains late !", "else ○". The symbol is the last word.</summary>
    public static IndicatorRule Parse(string line)
    {
        var text = line.Trim();
        int lastSpace = text.LastIndexOf(' ');
        if (lastSpace <= 0)
            throw new FormatException($"'{line}' needs a test and a symbol, e.g. \">= 100 ✓\" or \"else ○\".");
        string symbol = text[(lastSpace + 1)..];
        string test = text[..lastSpace].Trim();
        if (test.Equals("else", StringComparison.OrdinalIgnoreCase))
            return new IndicatorRule(null, "", symbol);
        foreach (var (op, value) in Operators)
        {
            if (test.StartsWith(op, StringComparison.OrdinalIgnoreCase))
                return new IndicatorRule(value, test[op.Length..].Trim(), symbol);
        }
        throw new FormatException($"'{line}' should start with =, <>, <, <=, >, >=, contains or else.");
    }

    public override string ToString() =>
        Operator is not { } op ? $"else {Symbol}" : $"{Operators.First(o => o.Op == op).Text} {Value} {Symbol}";
}

/// <summary>
/// A user-defined task field, as in Microsoft Project: one of the fixed slots (Text1-30, Number1-20,
/// Date1-10, Flag1-20, Cost1-10, Duration1-10, OutlineCode1-10) given an alias and, optionally, a list
/// of allowed values (for an outline code, its lookup table of codes such as 1, 1.1, 1.2).
/// </summary>
public sealed class CustomField
{
    /// <summary>Outline code slots: text fields whose values are hierarchical codes from a lookup table.</summary>
    public const int OutlineCodeCount = 10;

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

    /// <summary>When set, the field is calculated from this formula (see <see cref="Fields.Formula"/>) and cannot be typed into.</summary>
    public string? Formula { get; set; }

    public bool IsCalculated => !string.IsNullOrWhiteSpace(Formula);

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Key : Alias;

    /// <summary>Graphical indicators: shown instead of the value, by the first matching rule.</summary>
    public List<IndicatorRule> Indicators { get; set; } = new();

    public bool IsOutlineCode => Key.StartsWith("OutlineCode", StringComparison.Ordinal);

    /// <summary>The indicator symbol for a task, or null to show the value.</summary>
    public string? IndicatorFor(Model.Project project, Model.ProjectTask task)
    {
        foreach (var rule in Indicators)
        {
            if (rule.Operator is not { } op || new Views.FilterDefinition("Indicator", new Views.FilterCondition(Key, op, rule.Value)).Matches(project, task))
                return rule.Symbol;
        }
        return null;
    }

    /// <summary>All slot keys, e.g. Text1..Text30, Number1..Number20, OutlineCode1..OutlineCode10.</summary>
    public static IEnumerable<string> AllSlots() =>
        SlotCounts.SelectMany(kv => Enumerable.Range(1, kv.Value).Select(i => $"{kv.Key}{i}"))
            .Concat(Enumerable.Range(1, OutlineCodeCount).Select(i => $"OutlineCode{i}"));

    /// <summary>Slot groups as offered in the Custom Fields dialog: a label, the key prefix and the count.</summary>
    public static IReadOnlyList<(string Label, string Prefix, int Count)> SlotKinds { get; } =
        SlotCounts.Select(kv => (kv.Key.ToString(), kv.Key.ToString(), kv.Value))
            .Append(("Outline Code", "OutlineCode", OutlineCodeCount)).ToList();

    public static FieldType? TypeOfKey(string key)
    {
        if (key.StartsWith("OutlineCode", StringComparison.Ordinal))
            return int.TryParse(key["OutlineCode".Length..], out int code) && code >= 1 && code <= OutlineCodeCount ? FieldType.Text : null;

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
