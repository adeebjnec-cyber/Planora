using System.Globalization;
using System.Text;
using Planora.Engine.Model;

namespace Planora.Engine.Fields;

public sealed class FormulaException : Exception
{
    public FormulaException(string message) : base(message) { }
}

/// <summary>
/// A custom field formula, in the style of Microsoft Project's formulas:
/// <c>IIf([Cost] &gt; [Baseline Cost], "Over budget", "OK")</c>, <c>[Finish] - [Start]</c>,
/// <c>ProjDateDiff([Start], [Deadline]) / 480</c>.
/// <para>
/// Field references use the field's name or key in square brackets (custom fields by alias or slot).
/// Durations and work are numbers of minutes, costs and percentages are numbers, flags are True/False,
/// dates are dates (subtracting two dates gives days). Operators: + - * / &amp; (text join),
/// = &lt;&gt; &lt; &gt; &lt;= &gt;=, And, Or, Not. Functions: IIf, Abs, Round, Int, Min, Max, Len, Left, Right,
/// Mid, UCase, LCase, Trim, Str, Val, Now, Date, Year, Month, Day, DateAdd (days), ProjDateDiff
/// (working minutes between two dates on the project calendar), IsNull.
/// </para>
/// </summary>
public sealed class Formula
{
    private const int MaxDepth = 20;
    private static readonly Dictionary<string, Formula> Cache = new();
    [ThreadStatic] private static int _depth;

    private readonly Func<Context, object?> _root;

    private Formula(string text, Func<Context, object?> root)
    {
        Text = text;
        _root = root;
    }

    public string Text { get; }

    /// <summary>True while a formula is being evaluated (so nested formula errors propagate to the outermost one).</summary>
    internal static bool IsNested => _depth > 0;

    /// <summary>Parses (and caches) a formula. Throws <see cref="FormulaException"/> on syntax errors.</summary>
    public static Formula Parse(string text)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(text, out var cached))
                return cached;
        }
        var parser = new Parser(text);
        var root = parser.ParseExpression();
        parser.ExpectEnd();
        var formula = new Formula(text, root);
        lock (Cache)
            Cache[text] = formula;
        return formula;
    }

    /// <summary>Evaluates for a task. Returns null for missing values; throws <see cref="FormulaException"/> on errors.</summary>
    public object? Evaluate(Project project, ProjectTask task)
    {
        if (_depth > MaxDepth)
            throw new FormulaException("Formula fields refer to each other in a loop.");
        _depth++;
        try
        {
            return _root(new Context(project, task));
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>Converts a formula result to the value type of a field.</summary>
    public static object? Convert(object? value, FieldType type)
    {
        if (value is null)
            return null;
        try
        {
            return type switch
            {
                FieldType.Text => ToText(value),
                FieldType.Flag => ToBool(value),
                FieldType.Date => value is DateTime d ? d : null,
                FieldType.Cost => (decimal)ToNumber(value),
                FieldType.Duration or FieldType.Work => (long)Math.Round(ToNumber(value)),
                _ => ToNumber(value),
            };
        }
        catch (FormulaException)
        {
            return null;
        }
    }

    private sealed record Context(Project Project, ProjectTask Task);

    // ------------------------------------------------------------------ value helpers

    private static double ToNumber(object? value) => value switch
    {
        null => 0,
        double d => d,
        long l => l,
        int i => i,
        decimal m => (double)m,
        bool b => b ? -1 : 0, // as in Microsoft Project / VBA, True is -1
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var n) => n,
        DateTime d => d.ToOADate(),
        _ => throw new FormulaException($"'{value}' is not a number."),
    };

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase),
        _ => ToNumber(value) != 0,
    };

    private static string ToText(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "Yes" : "No",
        DateTime d => d.ToString("d", CultureInfo.CurrentCulture),
        double d => d.ToString("0.##", CultureInfo.CurrentCulture),
        decimal m => m.ToString("0.##", CultureInfo.CurrentCulture),
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };

    private static int Compare(object? a, object? b)
    {
        if (a is string || b is string)
            return string.Compare(ToText(a), ToText(b), StringComparison.CurrentCultureIgnoreCase);
        if (a is DateTime x && b is DateTime y)
            return x.CompareTo(y);
        return ToNumber(a).CompareTo(ToNumber(b));
    }

    // ------------------------------------------------------------------ parser

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text) => _text = text;

        public void ExpectEnd()
        {
            SkipSpace();
            if (_pos < _text.Length)
                throw Error($"Unexpected '{_text[_pos..]}'.");
        }

        public Func<Context, object?> ParseExpression() => ParseOr();

        private Func<Context, object?> ParseOr()
        {
            var left = ParseAnd();
            while (Keyword("Or"))
            {
                var (l, r) = (left, ParseAnd());
                left = c => ToBool(l(c)) || ToBool(r(c));
            }
            return left;
        }

        private Func<Context, object?> ParseAnd()
        {
            var left = ParseNot();
            while (Keyword("And"))
            {
                var (l, r) = (left, ParseNot());
                left = c => ToBool(l(c)) && ToBool(r(c));
            }
            return left;
        }

        private Func<Context, object?> ParseNot()
        {
            if (Keyword("Not"))
            {
                var operand = ParseNot();
                return c => !ToBool(operand(c));
            }
            return ParseComparison();
        }

        private Func<Context, object?> ParseComparison()
        {
            var left = ParseConcat();
            foreach (var op in new[] { "<=", ">=", "<>", "=", "<", ">" })
            {
                if (!Symbol(op))
                    continue;
                var right = ParseConcat();
                return op switch
                {
                    "=" => c => Compare(left(c), right(c)) == 0,
                    "<>" => c => Compare(left(c), right(c)) != 0,
                    "<" => c => Compare(left(c), right(c)) < 0,
                    ">" => c => Compare(left(c), right(c)) > 0,
                    "<=" => c => Compare(left(c), right(c)) <= 0,
                    _ => c => Compare(left(c), right(c)) >= 0,
                };
            }
            return left;
        }

        private Func<Context, object?> ParseConcat()
        {
            var left = ParseAdditive();
            while (Symbol("&"))
            {
                var (l, r) = (left, ParseAdditive());
                left = c => ToText(l(c)) + ToText(r(c));
            }
            return left;
        }

        private Func<Context, object?> ParseAdditive()
        {
            var left = ParseTerm();
            while (true)
            {
                if (Symbol("+"))
                {
                    var (l, r) = (left, ParseTerm());
                    left = c => Add(l(c), r(c), 1);
                }
                else if (Symbol("-"))
                {
                    var (l, r) = (left, ParseTerm());
                    left = c => Add(l(c), r(c), -1);
                }
                else
                    return left;
            }
        }

        private static object? Add(object? a, object? b, int sign)
        {
            if (a is DateTime da && b is DateTime db && sign < 0)
                return (da - db).TotalDays;
            if (a is DateTime d)
                return d.AddDays(sign * ToNumber(b));
            if (a is string sa && sign > 0)
                return sa + ToText(b);
            return ToNumber(a) + sign * ToNumber(b);
        }

        private Func<Context, object?> ParseTerm()
        {
            var left = ParseUnary();
            while (true)
            {
                if (Symbol("*"))
                {
                    var (l, r) = (left, ParseUnary());
                    left = c => ToNumber(l(c)) * ToNumber(r(c));
                }
                else if (Symbol("/"))
                {
                    var (l, r) = (left, ParseUnary());
                    left = c =>
                    {
                        double divisor = ToNumber(r(c));
                        if (divisor == 0)
                            throw new FormulaException("Division by zero.");
                        return ToNumber(l(c)) / divisor;
                    };
                }
                else
                    return left;
            }
        }

        private Func<Context, object?> ParseUnary()
        {
            if (Symbol("-"))
            {
                var operand = ParseUnary();
                return c => -ToNumber(operand(c));
            }
            return ParsePrimary();
        }

        private Func<Context, object?> ParsePrimary()
        {
            SkipSpace();
            if (_pos >= _text.Length)
                throw Error("The formula ends too early.");
            char ch = _text[_pos];

            if (ch == '(')
            {
                _pos++;
                var inner = ParseExpression();
                Expect(")");
                return inner;
            }
            if (ch == '"')
            {
                var sb = new StringBuilder();
                _pos++;
                while (true)
                {
                    if (_pos >= _text.Length)
                        throw Error("A text value is missing its closing quote.");
                    if (_text[_pos] == '"')
                    {
                        if (_pos + 1 < _text.Length && _text[_pos + 1] == '"')
                        {
                            sb.Append('"');
                            _pos += 2;
                            continue;
                        }
                        _pos++;
                        break;
                    }
                    sb.Append(_text[_pos++]);
                }
                var value = sb.ToString();
                return _ => value;
            }
            if (ch == '[')
            {
                int end = _text.IndexOf(']', _pos);
                if (end < 0)
                    throw Error("A field name is missing its closing ']'.");
                var name = _text[(_pos + 1)..end].Trim();
                _pos = end + 1;
                return c => FieldValue(c, name);
            }
            if (char.IsDigit(ch) || ch == '.')
            {
                int start = _pos;
                while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                    _pos++;
                var number = double.Parse(_text[start.._pos], CultureInfo.InvariantCulture);
                return _ => number;
            }
            if (char.IsLetter(ch))
            {
                int start = _pos;
                while (_pos < _text.Length && char.IsLetterOrDigit(_text[_pos]))
                    _pos++;
                var word = _text[start.._pos];
                if (word.Equals("True", StringComparison.OrdinalIgnoreCase))
                    return _ => true;
                if (word.Equals("False", StringComparison.OrdinalIgnoreCase))
                    return _ => false;
                var args = new List<Func<Context, object?>>();
                if (Symbol("("))
                {
                    if (!Symbol(")"))
                    {
                        do
                            args.Add(ParseExpression());
                        while (Symbol(","));
                        Expect(")");
                    }
                }
                return Function(word, args);
            }
            throw Error($"Unexpected '{ch}'.");
        }

        private static object? FieldValue(Context c, string name)
        {
            var field = TaskFields.All(c.Project).FirstOrDefault(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || f.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormulaException($"There is no field named [{name}].");
            return field.Get(c.Project, c.Task) switch
            {
                decimal m => (double)m,
                long l => (double)l,
                var v => v,
            };
        }

        private Func<Context, object?> Function(string name, List<Func<Context, object?>> args)
        {
            void Count(int min, int max)
            {
                if (args.Count < min || args.Count > max)
                    throw Error($"{name} takes {(min == max ? min.ToString() : $"{min} to {max}")} argument(s).");
            }
            object? Arg(Context c, int i) => args[i](c);

            switch (name.ToLowerInvariant())
            {
                case "iif":
                    Count(3, 3);
                    return c => ToBool(Arg(c, 0)) ? Arg(c, 1) : Arg(c, 2);
                case "abs":
                    Count(1, 1);
                    return c => Math.Abs(ToNumber(Arg(c, 0)));
                case "int":
                    Count(1, 1);
                    return c => Math.Floor(ToNumber(Arg(c, 0)));
                case "round":
                    Count(1, 2);
                    return c => Math.Round(ToNumber(Arg(c, 0)), args.Count > 1 ? (int)ToNumber(Arg(c, 1)) : 0, MidpointRounding.AwayFromZero);
                case "min":
                    Count(2, 2);
                    return c => Math.Min(ToNumber(Arg(c, 0)), ToNumber(Arg(c, 1)));
                case "max":
                    Count(2, 2);
                    return c => Math.Max(ToNumber(Arg(c, 0)), ToNumber(Arg(c, 1)));
                case "len":
                    Count(1, 1);
                    return c => (double)ToText(Arg(c, 0)).Length;
                case "left":
                    Count(2, 2);
                    return c => { var s = ToText(Arg(c, 0)); return s[..Math.Clamp((int)ToNumber(Arg(c, 1)), 0, s.Length)]; };
                case "right":
                    Count(2, 2);
                    return c => { var s = ToText(Arg(c, 0)); int n = Math.Clamp((int)ToNumber(Arg(c, 1)), 0, s.Length); return s[^n..]; };
                case "mid":
                    Count(2, 3);
                    return c =>
                    {
                        var s = ToText(Arg(c, 0));
                        int start = Math.Clamp((int)ToNumber(Arg(c, 1)) - 1, 0, s.Length);
                        int length = args.Count > 2 ? Math.Clamp((int)ToNumber(Arg(c, 2)), 0, s.Length - start) : s.Length - start;
                        return s.Substring(start, length);
                    };
                case "ucase":
                    Count(1, 1);
                    return c => ToText(Arg(c, 0)).ToUpper(CultureInfo.CurrentCulture);
                case "lcase":
                    Count(1, 1);
                    return c => ToText(Arg(c, 0)).ToLower(CultureInfo.CurrentCulture);
                case "trim":
                    Count(1, 1);
                    return c => ToText(Arg(c, 0)).Trim();
                case "str":
                    Count(1, 1);
                    return c => ToText(Arg(c, 0));
                case "val":
                    Count(1, 1);
                    return c => double.TryParse(ToText(Arg(c, 0)), NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v : 0.0;
                case "isnull":
                    Count(1, 1);
                    return c => Arg(c, 0) is null;
                case "now":
                    Count(0, 0);
                    return _ => DateTime.Now;
                case "date":
                    Count(0, 0);
                    return _ => DateTime.Today;
                case "year":
                case "month":
                case "day":
                    Count(1, 1);
                    var part = name.ToLowerInvariant();
                    return c => Arg(c, 0) is DateTime d ? (double)(part == "year" ? d.Year : part == "month" ? d.Month : d.Day) : null;
                case "dateadd":
                    Count(2, 2);
                    return c => Arg(c, 0) is DateTime d ? d.AddDays(ToNumber(Arg(c, 1))) : null;
                case "projdatediff":
                    Count(2, 2);
                    return c => Arg(c, 0) is DateTime a && Arg(c, 1) is DateTime b ? (double)c.Project.Calendar.WorkingMinutesBetween(a, b) : null;
                default:
                    throw Error($"Unknown function '{name}'.");
            }
        }

        private bool Keyword(string word)
        {
            SkipSpace();
            if (_pos + word.Length <= _text.Length &&
                string.Compare(_text, _pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                (_pos + word.Length == _text.Length || !char.IsLetterOrDigit(_text[_pos + word.Length])))
            {
                _pos += word.Length;
                return true;
            }
            return false;
        }

        private bool Symbol(string symbol)
        {
            SkipSpace();
            if (string.CompareOrdinal(_text, _pos, symbol, 0, symbol.Length) == 0)
            {
                // Do not read "<" from "<=" or "<>".
                if (symbol is "<" or ">" && _pos + 1 < _text.Length && _text[_pos + 1] is '=' or '>')
                    return false;
                _pos += symbol.Length;
                return true;
            }
            return false;
        }

        private void Expect(string symbol)
        {
            if (!Symbol(symbol))
                throw Error($"Expected '{symbol}'.");
        }

        private void SkipSpace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                _pos++;
        }

        private FormulaException Error(string message) => new($"{message} (at position {_pos + 1})");
    }
}
