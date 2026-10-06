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
/// ^, Mod, \ (integer division), = &lt;&gt; &lt; &gt; &lt;= &gt;=, And, Or, Not. Functions follow Microsoft Project's formula
/// reference: maths (Abs, Int, Fix, Round, Sgn, Sqr, Exp, Log, trigonometry, Min, Max), conversion (CBool, CDbl,
/// CInt, CStr, CDate, Val, Str, Asc, Chr, Hex, IsNumeric, IsDate, IsNull), text (Len, Left, Right, Mid, InStr, Replace,
/// UCase, LCase, Trim, LTrim, RTrim, Space, String, StrComp, StrReverse, Format), dates (Now, Date, Time, Year, Month,
/// Day, Hour, Minute, Second, Weekday, WeekdayName, MonthName, DateAdd, DateDiff, DatePart, DateSerial, TimeSerial),
/// choices (IIf, Choose, Switch) and project functions (ProjDateDiff, ProjDateAdd, ProjDateSub, ProjDateValue,
/// ProjDurValue, ProjDurConv with pjMinutes/pjHours/pjDays/pjWeeks/pjMonths).
/// </para>
/// </summary>
/// <summary>Variables visible to an expression (used by macros); formula fields have none.</summary>
public interface IVariableScope
{
    bool TryGet(string name, out object? value);

    /// <summary>Reads a member of a value, e.g. a task's or resource's field ("t.Name", "r.[Max Units]").</summary>
    object? Member(object? target, string member);
}

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
    public static Formula Parse(string text) => Parse(text, allowVariables: false);

    /// <summary>
    /// Parses an expression; with <paramref name="allowVariables"/> (macros), names that are not functions are
    /// variables and "name.member" reads a member of a variable's value.
    /// </summary>
    public static Formula Parse(string text, bool allowVariables)
    {
        var key = allowVariables ? "\u0001" + text : text;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
        }
        var parser = new Parser(text, allowVariables);
        var root = parser.ParseExpression();
        parser.ExpectEnd();
        var formula = new Formula(text, root);
        lock (Cache)
            Cache[key] = formula;
        return formula;
    }

    /// <summary>Evaluates for a task. Returns null for missing values; throws <see cref="FormulaException"/> on errors.</summary>
    public object? Evaluate(Project project, ProjectTask task) => Evaluate(project, task, null);

    /// <summary>Evaluates with an optional current task and variables (macros).</summary>
    public object? Evaluate(Project project, ProjectTask? task, IVariableScope? scope)
    {
        if (_depth > MaxDepth)
            throw new FormulaException("Formula fields refer to each other in a loop.");
        _depth++;
        try
        {
            return _root(new Context(project, task, scope));
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

    private sealed record Context(Project Project, ProjectTask? Task, IVariableScope? Scope = null);

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

    private static DateTime? ToDate(object? value) => value switch
    {
        null => null,
        DateTime d => d,
        double n => DateTime.FromOADate(n),
        string s when DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d) => d,
        _ => throw new FormulaException($"'{value}' is not a date."),
    };

    /// <summary>Durations in formulas are minutes; text such as "3d" is parsed with the project's settings.</summary>
    private static double DurationMinutes(Context c, object? value) =>
        value is string text
            ? DurationText.TryParse(text, c.Project.Options, out var d) ? d.Minutes : throw new FormulaException($"'{text}' is not a duration.")
            : ToNumber(value);

    private static DurationUnit UnitFor(int pjUnit) => pjUnit switch
    {
        3 or 4 => DurationUnit.Minutes,
        5 or 6 => DurationUnit.Hours,
        9 or 10 => DurationUnit.Weeks,
        11 or 12 => DurationUnit.Months,
        _ => DurationUnit.Days,
    };

    private static DateTime AddInterval(string interval, double number, DateTime date) => interval.ToLowerInvariant() switch
    {
        "yyyy" => date.AddYears((int)number),
        "q" => date.AddMonths(3 * (int)number),
        "m" => date.AddMonths((int)number),
        "ww" => date.AddDays(7 * number),
        "h" => date.AddHours(number),
        "n" => date.AddMinutes(number),
        "s" => date.AddSeconds(number),
        "d" or "y" or "w" => date.AddDays(number),
        _ => throw new FormulaException($"'{interval}' is not a date interval (use yyyy, q, m, ww, d, h, n or s)."),
    };

    private static double DiffInterval(string interval, DateTime a, DateTime b) => interval.ToLowerInvariant() switch
    {
        "yyyy" => b.Year - a.Year,
        "q" => (b.Year - a.Year) * 4 + (b.Month - 1) / 3 - (a.Month - 1) / 3,
        "m" => (b.Year - a.Year) * 12 + b.Month - a.Month,
        "ww" or "w" => Math.Floor((b.Date - a.Date).TotalDays / 7),
        "h" => Math.Floor((b - a).TotalHours),
        "n" => Math.Floor((b - a).TotalMinutes),
        "s" => Math.Floor((b - a).TotalSeconds),
        "d" or "y" => (b.Date - a.Date).TotalDays,
        _ => throw new FormulaException($"'{interval}' is not a date interval (use yyyy, q, m, ww, d, h, n or s)."),
    };

    private static double DatePart(string interval, DateTime d) => interval.ToLowerInvariant() switch
    {
        "yyyy" => d.Year,
        "q" => (d.Month - 1) / 3 + 1,
        "m" => d.Month,
        "y" => d.DayOfYear,
        "d" => d.Day,
        "w" => (int)d.DayOfWeek + 1,
        "ww" => CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstDay, DayOfWeek.Sunday),
        "h" => d.Hour,
        "n" => d.Minute,
        "s" => d.Second,
        _ => throw new FormulaException($"'{interval}' is not a date interval."),
    };

    /// <summary>VBA-style Format for common cases: named formats and .NET-compatible patterns.</summary>
    private static string FormatValue(object? value, string? format)
    {
        if (format is null)
            return ToText(value);
        var named = format.ToLowerInvariant();
        return value switch
        {
            DateTime d => named switch
            {
                "short date" => d.ToString("d", CultureInfo.CurrentCulture),
                "long date" => d.ToString("D", CultureInfo.CurrentCulture),
                "short time" => d.ToString("t", CultureInfo.CurrentCulture),
                "long time" => d.ToString("T", CultureInfo.CurrentCulture),
                _ => d.ToString(format.Replace("nn", "mm"), CultureInfo.CurrentCulture),
            },
            null => "",
            string s => s,
            _ => named switch
            {
                "currency" => ((decimal)ToNumber(value)).ToString("C", CultureInfo.CurrentCulture),
                "percent" => (ToNumber(value) * 100).ToString("0.00", CultureInfo.CurrentCulture) + "%",
                "fixed" => ToNumber(value).ToString("0.00", CultureInfo.CurrentCulture),
                "standard" => ToNumber(value).ToString("#,##0.00", CultureInfo.CurrentCulture),
                "general number" => ToNumber(value).ToString(CultureInfo.CurrentCulture),
                "yes/no" => ToBool(value) ? "Yes" : "No",
                "true/false" => ToBool(value) ? "True" : "False",
                _ => ToNumber(value).ToString(format, CultureInfo.CurrentCulture),
            },
        };
    }
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
        private readonly bool _allowVariables;
        private int _pos;

        public Parser(string text, bool allowVariables = false)
        {
            _text = text;
            _allowVariables = allowVariables;
        }

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
                else if (Symbol("\\"))
                {
                    var (l, r) = (left, ParseUnary());
                    left = c =>
                    {
                        double divisor = Math.Round(ToNumber(r(c)));
                        if (divisor == 0)
                            throw new FormulaException("Division by zero.");
                        return Math.Truncate(Math.Round(ToNumber(l(c))) / divisor);
                    };
                }
                else if (Keyword("Mod"))
                {
                    var (l, r) = (left, ParseUnary());
                    left = c =>
                    {
                        double divisor = Math.Round(ToNumber(r(c)));
                        if (divisor == 0)
                            throw new FormulaException("Division by zero.");
                        return Math.Round(ToNumber(l(c))) % divisor;
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
            return ParsePower();
        }

        /// <summary>Exponentiation binds tightest, as in VBA: 2 ^ 3 ^ 2 = 2 ^ 9.</summary>
        private Func<Context, object?> ParsePower()
        {
            var left = ParsePrimary();
            if (!Symbol("^"))
                return left;
            var right = ParseUnary();
            return c => Math.Pow(ToNumber(left(c)), ToNumber(right(c)));
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
                if (_allowVariables && word.Equals("Nothing", StringComparison.OrdinalIgnoreCase))
                    return _ => null;
                if (_allowVariables && _pos < _text.Length && _text[_pos] == '.')
                {
                    // name.member or name.[Field Name], possibly chained (t.Parent.Name).
                    Func<Context, object?> target = c => Variable(c, word);
                    while (_pos < _text.Length && _text[_pos] == '.')
                    {
                        _pos++;
                        string member;
                        if (_pos < _text.Length && _text[_pos] == '[')
                        {
                            int end = _text.IndexOf(']', _pos);
                            if (end < 0)
                                throw Error("A field name is missing its closing ']'.");
                            member = _text[(_pos + 1)..end].Trim();
                            _pos = end + 1;
                        }
                        else
                        {
                            int m = _pos;
                            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
                                _pos++;
                            member = _text[m.._pos];
                            if (member.Length == 0)
                                throw Error("A name is expected after '.'.");
                        }
                        var inner = target;
                        target = c => c.Scope is { } scope ? scope.Member(inner(c), member) : throw new FormulaException($"'.{member}' needs a value.");
                    }
                    return target;
                }
                var args = new List<Func<Context, object?>>();
                if (_allowVariables && !(SkipSpaceThen('(')))
                {
                    // Without parentheses: a constant function (pjDays) if one exists, otherwise a variable.
                    try
                    {
                        return Function(word, args);
                    }
                    catch (FormulaException)
                    {
                        return c => Variable(c, word);
                    }
                }
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

        private bool SkipSpaceThen(char ch)
        {
            int p = _pos;
            while (p < _text.Length && char.IsWhiteSpace(_text[p]))
                p++;
            return p < _text.Length && _text[p] == ch;
        }

        private static object? Variable(Context c, string name)
        {
            if (c.Scope is { } scope && scope.TryGet(name, out var value))
                return value;
            throw new FormulaException($"'{name}' is not defined.");
        }

        private static object? FieldValue(Context c, string name)
        {
            if (c.Task is null)
                throw new FormulaException($"[{name}] needs a current task (use it inside For Each t In Tasks, or write t.[{name}]).");
            var field = TaskFields.All(c.Project).FirstOrDefault(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || f.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormulaException($"There is no field named [{name}].");
            return field.Get(c.Project, c.Task!) switch
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
                    Count(2, 3);
                    // DateAdd(interval, number, date) as in VBA, or DateAdd(date, days).
                    if (args.Count == 3)
                        return c => ToDate(Arg(c, 2)) is { } d ? AddInterval(ToText(Arg(c, 0)), ToNumber(Arg(c, 1)), d) : null;
                    return c => Arg(c, 0) is DateTime d ? d.AddDays(ToNumber(Arg(c, 1))) : null;
                case "sgn":
                    Count(1, 1);
                    return c => (double)Math.Sign(ToNumber(Arg(c, 0)));
                case "sqr":
                    Count(1, 1);
                    return c => Math.Sqrt(ToNumber(Arg(c, 0)));
                case "exp":
                    Count(1, 1);
                    return c => Math.Exp(ToNumber(Arg(c, 0)));
                case "log":
                    Count(1, 1);
                    return c => Math.Log(ToNumber(Arg(c, 0)));
                case "fix":
                    Count(1, 1);
                    return c => Math.Truncate(ToNumber(Arg(c, 0)));
                case "atn":
                    Count(1, 1);
                    return c => Math.Atan(ToNumber(Arg(c, 0)));
                case "sin":
                    Count(1, 1);
                    return c => Math.Sin(ToNumber(Arg(c, 0)));
                case "cos":
                    Count(1, 1);
                    return c => Math.Cos(ToNumber(Arg(c, 0)));
                case "tan":
                    Count(1, 1);
                    return c => Math.Tan(ToNumber(Arg(c, 0)));

                // ---- conversion ----
                case "cbool":
                    Count(1, 1);
                    return c => ToBool(Arg(c, 0));
                case "cdbl" or "csng" or "ccur" or "cdec":
                    Count(1, 1);
                    return c => ToNumber(Arg(c, 0));
                case "cint" or "clng":
                    Count(1, 1);
                    return c => Math.Round(ToNumber(Arg(c, 0)), MidpointRounding.ToEven);
                case "cstr":
                    Count(1, 1);
                    return c => ToText(Arg(c, 0));
                case "cdate" or "datevalue":
                    Count(1, 1);
                    return c => ToDate(Arg(c, 0));
                case "asc":
                    Count(1, 1);
                    return c => ToText(Arg(c, 0)) is { Length: > 0 } s ? (double)s[0] : throw new FormulaException("Asc needs a non-empty text.");
                case "chr":
                    Count(1, 1);
                    return c => ((char)(int)ToNumber(Arg(c, 0))).ToString();
                case "hex":
                    Count(1, 1);
                    return c => ((long)Math.Round(ToNumber(Arg(c, 0)))).ToString("X", CultureInfo.InvariantCulture);
                case "isnumeric":
                    Count(1, 1);
                    return c => Arg(c, 0) is double or long or int or decimal ||
                                double.TryParse(ToText(Arg(c, 0)), NumberStyles.Float, CultureInfo.CurrentCulture, out _);
                case "isdate":
                    Count(1, 1);
                    return c => Arg(c, 0) is DateTime || DateTime.TryParse(ToText(Arg(c, 0)), CultureInfo.CurrentCulture, DateTimeStyles.None, out _);

                // ---- text ----
                case "instr":
                    Count(2, 3);
                    return c =>
                    {
                        int start = args.Count == 3 ? Math.Max(1, (int)ToNumber(Arg(c, 0))) : 1;
                        var text = ToText(Arg(c, args.Count - 2));
                        var find = ToText(Arg(c, args.Count - 1));
                        if (start > text.Length + 1)
                            return 0.0;
                        int index = text.IndexOf(find, start - 1, StringComparison.CurrentCultureIgnoreCase);
                        return (double)(index + 1);
                    };
                case "replace":
                    Count(3, 3);
                    return c => ToText(Arg(c, 0)).Replace(ToText(Arg(c, 1)), ToText(Arg(c, 2)), StringComparison.CurrentCultureIgnoreCase);
                case "ltrim":
                    Count(1, 1);
                    return c => ToText(Arg(c, 0)).TrimStart();
                case "rtrim":
                    Count(1, 1);
                    return c => ToText(Arg(c, 0)).TrimEnd();
                case "space":
                    Count(1, 1);
                    return c => new string(' ', Math.Clamp((int)ToNumber(Arg(c, 0)), 0, 10000));
                case "string":
                    Count(2, 2);
                    return c => ToText(Arg(c, 1)) is { Length: > 0 } s ? new string(s[0], Math.Clamp((int)ToNumber(Arg(c, 0)), 0, 10000)) : "";
                case "strreverse":
                    Count(1, 1);
                    return c => new string(ToText(Arg(c, 0)).Reverse().ToArray());
                case "strcomp":
                    Count(2, 3);
                    return c => (double)Math.Sign(string.Compare(ToText(Arg(c, 0)), ToText(Arg(c, 1)),
                        args.Count == 3 && ToNumber(Arg(c, 2)) == 0 ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase));
                case "format":
                    Count(1, 2);
                    return c => FormatValue(Arg(c, 0), args.Count > 1 ? ToText(Arg(c, 1)) : null);

                // ---- dates (VBA style) ----
                case "datediff":
                    Count(3, 3);
                    return c => ToDate(Arg(c, 1)) is { } a && ToDate(Arg(c, 2)) is { } b ? DiffInterval(ToText(Arg(c, 0)), a, b) : null;
                case "datepart":
                    Count(2, 2);
                    return c => ToDate(Arg(c, 1)) is { } d ? DatePart(ToText(Arg(c, 0)), d) : null;
                case "dateserial":
                    Count(3, 3);
                    return c => new DateTime((int)ToNumber(Arg(c, 0)), 1, 1).AddMonths((int)ToNumber(Arg(c, 1)) - 1).AddDays(ToNumber(Arg(c, 2)) - 1);
                case "timeserial":
                    Count(3, 3);
                    return c => DateTime.MinValue.AddHours(ToNumber(Arg(c, 0))).AddMinutes(ToNumber(Arg(c, 1))).AddSeconds(ToNumber(Arg(c, 2)));
                case "hour" or "minute" or "second":
                    Count(1, 1);
                    var timePart = name.ToLowerInvariant();
                    return c => ToDate(Arg(c, 0)) is { } d ? (double)(timePart == "hour" ? d.Hour : timePart == "minute" ? d.Minute : d.Second) : null;
                case "weekday":
                    Count(1, 2);
                    return c =>
                    {
                        if (ToDate(Arg(c, 0)) is not { } d)
                            return null;
                        int firstDay = args.Count > 1 ? Math.Clamp((int)ToNumber(Arg(c, 1)), 1, 7) : 1; // 1 = Sunday
                        return (double)(((int)d.DayOfWeek - (firstDay - 1) + 7) % 7 + 1);
                    };
                case "weekdayname":
                    Count(1, 2);
                    return c =>
                    {
                        var day = (DayOfWeek)((Math.Clamp((int)ToNumber(Arg(c, 0)), 1, 7) - 1) % 7);
                        return args.Count > 1 && ToBool(Arg(c, 1))
                            ? CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day)
                            : CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);
                    };
                case "monthname":
                    Count(1, 2);
                    return c =>
                    {
                        int month = Math.Clamp((int)ToNumber(Arg(c, 0)), 1, 12);
                        return args.Count > 1 && ToBool(Arg(c, 1))
                            ? CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month)
                            : CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
                    };
                case "time":
                    Count(0, 0);
                    return _ => DateTime.MinValue + DateTime.Now.TimeOfDay;

                // ---- choices ----
                case "choose":
                    if (args.Count < 2)
                        throw Error("Choose takes an index and at least one choice.");
                    return c =>
                    {
                        int index = (int)ToNumber(Arg(c, 0));
                        return index >= 1 && index < args.Count ? Arg(c, index) : null;
                    };
                case "switch":
                    if (args.Count < 2 || args.Count % 2 != 0)
                        throw Error("Switch takes pairs of conditions and values.");
                    return c =>
                    {
                        for (int i = 0; i < args.Count; i += 2)
                        {
                            if (ToBool(Arg(c, i)))
                                return Arg(c, i + 1);
                        }
                        return null;
                    };

                // ---- project ----
                case "projdateadd":
                case "projdatesub":
                    Count(2, 3);
                    int sign = name.Equals("projdatesub", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
                    return c => ToDate(Arg(c, 0)) is { } d
                        ? c.Project.Calendar.AddWorkingMinutes(d, (long)Math.Round(sign * DurationMinutes(c, Arg(c, 1))))
                        : null;
                case "projdatevalue":
                    Count(1, 1);
                    return c => ToDate(Arg(c, 0));
                case "projdurvalue":
                    Count(1, 1);
                    return c => DurationMinutes(c, Arg(c, 0));
                case "projdurconv":
                    Count(2, 2);
                    return c => DurationText.Format((long)Math.Round(DurationMinutes(c, Arg(c, 0))), c.Project.Options, UnitFor((int)ToNumber(Arg(c, 1))));
                case "pjminutes":
                    Count(0, 0);
                    return _ => 3.0;
                case "pjhours":
                    Count(0, 0);
                    return _ => 5.0;
                case "pjdays":
                    Count(0, 0);
                    return _ => 7.0;
                case "pjweeks":
                    Count(0, 0);
                    return _ => 9.0;
                case "pjmonths":
                    Count(0, 0);
                    return _ => 11.0;
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
