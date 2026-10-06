using System.Globalization;
using System.Text.RegularExpressions;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Macros;

/// <summary>A macro module saved with the project: VBA-style code with one or more Subs.</summary>
public sealed class MacroModule
{
    public string Name { get; set; } = "Module1";
    public string Code { get; set; } = "";
}

/// <summary>A macro error, with the line it happened on.</summary>
public sealed class MacroException : Exception
{
    public MacroException(int line, string message) : base(line > 0 ? $"Line {line}: {message}" : message) => Line = line;

    public int Line { get; }
}

/// <summary>What a macro run produced: message boxes and Debug.Print lines.</summary>
public sealed record MacroResult(IReadOnlyList<string> Messages, IReadOnlyList<string> Output, int StatementsRun);

/// <summary>
/// Planora's macro language: a VBA-like subset for automating a project without any add-ins.
/// <code>
/// Sub MarkLongTasks()
///     For Each t In ActiveProject.Tasks
///         If Not t.Summary And t.Duration > 5 * 480 Then
///             t.Marked = True
///             t.Text1 = "Long"
///         End If
///     Next t
///     MsgBox "Done"
/// End Sub
/// </code>
/// Supported: Sub/End Sub, Dim, assignments (x = ..., Set x = ..., t.Field = ..., [Field] = ...),
/// For Each ... In Tasks/Resources/SelectedTasks/t.Children/t.Assignments ... Next, For i = a To b [Step s] ... Next,
/// If/ElseIf/Else/End If (also on one line), Do While/Until ... Loop, Exit For/Do/Sub, Call Sub, MsgBox,
/// Debug.Print, Tasks.Add, Resources.Add, t.Delete, LinkTasks, UnlinkTasks, AssignResource, Indent, Outdent,
/// CalculateProject. Expressions use the formula language (operators, IIf, Left, DateAdd, ProjDateAdd...).
/// Durations and work are in minutes, as in Microsoft Project VBA.
/// </summary>
public static class Macro
{
    public const int MaxStatements = 2_000_000;

    /// <summary>The Subs defined in a module's code, in order.</summary>
    public static IReadOnlyList<string> SubNames(string code) =>
        Regex.Matches(code, @"^\s*(?:Public\s+|Private\s+)?Sub\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value).ToList();

    /// <summary>
    /// Runs a Sub (or, with no Subs in the code, the whole code). <paramref name="onMessage"/> shows MsgBox
    /// text as it happens. The project is rescheduled whenever the macro reads values after changing it.
    /// </summary>
    public static MacroResult Run(Project project, string code, string? subName = null, IReadOnlyList<ProjectTask>? selection = null, Action<string>? onMessage = null)
    {
        var program = Parser.ParseProgram(code);
        var interpreter = new Interpreter(project, program, selection ?? Array.Empty<ProjectTask>(), onMessage);
        var name = subName ?? program.Subs.Keys.FirstOrDefault() ?? "";
        if (program.Subs.Count == 0)
            interpreter.RunBlock(program.Main);
        else if (program.Subs.TryGetValue(name, out var body))
            interpreter.RunSub(body);
        else
            throw new MacroException(0, $"There is no Sub named '{name}'.");
        interpreter.Finish();
        return new MacroResult(interpreter.Messages, interpreter.Output, interpreter.Count);
    }

    // ------------------------------------------------------------------ syntax tree

    internal abstract record Statement(int Line);
    internal sealed record Assign(int Line, string Target, string Expression, bool IsSet) : Statement(Line);
    internal sealed record ForEach(int Line, string Variable, string Collection, List<Statement> Body) : Statement(Line);
    internal sealed record ForRange(int Line, string Variable, string From, string To, string? Step, List<Statement> Body) : Statement(Line);
    internal sealed record IfBlock(int Line, List<(string Condition, List<Statement> Body)> Branches, List<Statement>? Else) : Statement(Line);
    internal sealed record DoLoop(int Line, string? Condition, bool Until, bool TestAtEnd, List<Statement> Body) : Statement(Line);
    internal sealed record Command(int Line, string Name, string Arguments) : Statement(Line);
    internal sealed record Exit(int Line, string What) : Statement(Line);

    internal sealed class Program
    {
        public Dictionary<string, List<Statement>> Subs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Statement> Main { get; } = new();
    }

    // ------------------------------------------------------------------ parser

    private sealed class Parser
    {
        private readonly List<(int Line, string Text)> _lines;
        private int _index;

        private Parser(List<(int, string)> lines) => _lines = lines;

        public static Program ParseProgram(string code)
        {
            var parser = new Parser(Logical(code));
            var program = new Program();
            while (parser._index < parser._lines.Count)
            {
                var (line, text) = parser._lines[parser._index];
                var sub = Regex.Match(text, @"^(?:Public\s+|Private\s+)?Sub\s+([A-Za-z_][A-Za-z0-9_]*)\s*(\(\s*\))?\s*$", RegexOptions.IgnoreCase);
                if (sub.Success)
                {
                    parser._index++;
                    var body = parser.Block("End Sub");
                    parser.Expect(line, "End Sub");
                    if (!program.Subs.TryAdd(sub.Groups[1].Value, body))
                        throw new MacroException(line, $"Sub {sub.Groups[1].Value} is defined twice.");
                    continue;
                }
                program.Main.AddRange(parser.Block("End Sub"));
                if (parser._index < parser._lines.Count)
                    throw new MacroException(parser._lines[parser._index].Line, "'End Sub' without 'Sub'.");
            }
            return program;
        }

        /// <summary>Lines without comments, joined at " _" continuations and split at ':'.</summary>
        private static List<(int, string)> Logical(string code)
        {
            var result = new List<(int, string)>();
            var raw = code.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < raw.Length; i++)
            {
                int line = i + 1;
                var text = StripComment(raw[i]).TrimEnd();
                while (text.EndsWith(" _", StringComparison.Ordinal) && i + 1 < raw.Length)
                    text = text[..^2] + " " + StripComment(raw[++i]).Trim();
                foreach (var part in SplitStatements(text))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 0)
                        result.Add((line, trimmed));
                }
            }
            return result;
        }

        private static string StripComment(string text)
        {
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith("Rem ", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Rem", StringComparison.OrdinalIgnoreCase))
                return "";
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"')
                    quoted = !quoted;
                else if (text[i] == '\'' && !quoted)
                    return text[..i];
            }
            return text;
        }

        private static IEnumerable<string> SplitStatements(string text)
        {
            // Single-line If keeps its ':'-free parts together.
            if (Regex.IsMatch(text, @"^If\b.*\bThen\s+\S", RegexOptions.IgnoreCase))
            {
                yield return text;
                yield break;
            }
            bool quoted = false;
            int depth = 0, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"')
                    quoted = !quoted;
                else if (!quoted && ch == '[')
                    depth++;
                else if (!quoted && ch == ']')
                    depth--;
                else if (!quoted && depth == 0 && ch == ':')
                {
                    yield return text[start..i];
                    start = i + 1;
                }
            }
            yield return text[start..];
        }

        private bool AtEnd(params string[] enders)
        {
            if (_index >= _lines.Count)
                return true;
            var text = _lines[_index].Text;
            return enders.Any(e => StartsWithWord(text, e));
        }

        private static bool StartsWithWord(string text, string word) =>
            text.StartsWith(word, StringComparison.OrdinalIgnoreCase) && (text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]));

        private void Expect(int openedAt, string word)
        {
            if (_index >= _lines.Count || !StartsWithWord(_lines[_index].Text, word))
                throw new MacroException(openedAt, $"'{word}' is missing.");
            _index++;
        }

        private List<Statement> Block(params string[] enders)
        {
            var statements = new List<Statement>();
            while (!AtEnd(enders))
                statements.Add(Statement());
            return statements;
        }

        private Statement Statement()
        {
            var (line, text) = _lines[_index++];
            Match m;

            if ((m = Regex.Match(text, @"^For\s+Each\s+([A-Za-z_]\w*)\s+In\s+(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                var body = Block("Next");
                Expect(line, "Next");
                return new ForEach(line, m.Groups[1].Value, m.Groups[2].Value.Trim(), body);
            }
            if ((m = Regex.Match(text, @"^For\s+([A-Za-z_]\w*)\s*=\s*(.+?)\s+To\s+(.+?)(?:\s+Step\s+(.+))?$", RegexOptions.IgnoreCase)).Success)
            {
                var body = Block("Next");
                Expect(line, "Next");
                return new ForRange(line, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Success ? m.Groups[4].Value : null, body);
            }
            if ((m = Regex.Match(text, @"^If\s+(.+?)\s+Then\s*(.*)$", RegexOptions.IgnoreCase)).Success)
            {
                if (m.Groups[2].Value.Length > 0)
                    return SingleLineIf(line, m.Groups[1].Value, m.Groups[2].Value);
                var branches = new List<(string, List<Statement>)> { (m.Groups[1].Value, Block("ElseIf", "Else", "End If", "EndIf")) };
                List<Statement>? otherwise = null;
                while (true)
                {
                    if (_index >= _lines.Count)
                        throw new MacroException(line, "'End If' is missing.");
                    var next = _lines[_index].Text;
                    var elseIf = Regex.Match(next, @"^ElseIf\s+(.+?)\s+Then\s*$", RegexOptions.IgnoreCase);
                    if (elseIf.Success)
                    {
                        _index++;
                        branches.Add((elseIf.Groups[1].Value, Block("ElseIf", "Else", "End If", "EndIf")));
                    }
                    else if (StartsWithWord(next, "Else"))
                    {
                        _index++;
                        otherwise = Block("End If", "EndIf");
                    }
                    else
                    {
                        if (StartsWithWord(next, "EndIf"))
                            _index++;
                        else
                            Expect(line, "End If");
                        break;
                    }
                }
                return new IfBlock(line, branches, otherwise);
            }
            if ((m = Regex.Match(text, @"^Do(?:\s+(While|Until)\s+(.+))?$", RegexOptions.IgnoreCase)).Success)
            {
                var body = Block("Loop");
                var loopLine = _lines[Math.Min(_index, _lines.Count - 1)].Text;
                Expect(line, "Loop");
                var tail = Regex.Match(loopLine, @"^Loop\s+(While|Until)\s+(.+)$", RegexOptions.IgnoreCase);
                if (m.Groups[1].Success)
                    return new DoLoop(line, m.Groups[2].Value, m.Groups[1].Value.Equals("Until", StringComparison.OrdinalIgnoreCase), false, body);
                if (tail.Success)
                    return new DoLoop(line, tail.Groups[2].Value, tail.Groups[1].Value.Equals("Until", StringComparison.OrdinalIgnoreCase), true, body);
                return new DoLoop(line, null, false, false, body);
            }
            if ((m = Regex.Match(text, @"^While\s+(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                var body = Block("Wend");
                Expect(line, "Wend");
                return new DoLoop(line, m.Groups[1].Value, false, false, body);
            }
            if ((m = Regex.Match(text, @"^Exit\s+(For|Do|Sub)$", RegexOptions.IgnoreCase)).Success)
                return new Exit(line, m.Groups[1].Value.ToLowerInvariant());
            if (Regex.IsMatch(text, @"^Dim\b", RegexOptions.IgnoreCase))
                return new Command(line, "dim", text[3..].Trim());
            return Simple(line, text);
        }

        private Statement SingleLineIf(int line, string condition, string rest)
        {
            var split = Regex.Match(rest, @"^(.*?)\s+Else\s+(.*)$", RegexOptions.IgnoreCase);
            var then = split.Success ? split.Groups[1].Value : rest;
            var otherwise = split.Success ? new List<Statement> { Simple(line, split.Groups[2].Value.Trim()) } : null;
            return new IfBlock(line, new List<(string, List<Statement>)> { (condition, new List<Statement> { Simple(line, then.Trim()) }) }, otherwise);
        }

        /// <summary>Assignments and commands (MsgBox, Call, t.Delete, LinkTasks ...).</summary>
        private static Statement Simple(int line, string text)
        {
            Match m;
            if ((m = Regex.Match(text, @"^Exit\s+(For|Do|Sub)$", RegexOptions.IgnoreCase)).Success)
                return new Exit(line, m.Groups[1].Value.ToLowerInvariant());
            if ((m = Regex.Match(text, @"^Set\s+([A-Za-z_]\w*)\s*=\s*(.+)$", RegexOptions.IgnoreCase)).Success)
                return new Assign(line, m.Groups[1].Value, m.Groups[2].Value, true);
            // Target: name, name.member, name.[Field], or [Field] (current task).
            if ((m = Regex.Match(text, @"^((?:[A-Za-z_]\w*(?:\.(?:[A-Za-z_]\w*|\[[^\]]+\]))*)|\[[^\]]+\])\s*=\s*(.+)$")).Success
                && !Regex.IsMatch(m.Groups[1].Value, @"^(MsgBox|Call|Debug)$", RegexOptions.IgnoreCase))
                return new Assign(line, m.Groups[1].Value, m.Groups[2].Value, false);
            if ((m = Regex.Match(text, @"^(MsgBox|Debug\.Print|Print|Call|LinkTasks|UnlinkTasks|AssignResource|Indent|Outdent|CalculateProject)\b\s*(.*)$", RegexOptions.IgnoreCase)).Success)
                return new Command(line, m.Groups[1].Value.ToLowerInvariant(), StripParens(m.Groups[2].Value.Trim()));
            if ((m = Regex.Match(text, @"^([A-Za-z_]\w*)\.Delete\s*(\(\s*\))?$", RegexOptions.IgnoreCase)).Success)
                return new Command(line, "delete", m.Groups[1].Value);
            if ((m = Regex.Match(text, @"^([A-Za-z_]\w*)\s*(\(\s*\))?$")).Success)
                return new Command(line, "call", m.Groups[1].Value);
            throw new MacroException(line, $"Planora does not understand '{text}'.");
        }

        private static string StripParens(string text) =>
            text.StartsWith('(') && text.EndsWith(')') && Balanced(text[1..^1]) ? text[1..^1] : text;

        private static bool Balanced(string text)
        {
            int depth = 0;
            bool quoted = false;
            foreach (char ch in text)
            {
                if (ch == '"') quoted = !quoted;
                else if (!quoted && ch == '(') depth++;
                else if (!quoted && ch == ')' && --depth < 0) return false;
            }
            return depth == 0;
        }
    }

    // ------------------------------------------------------------------ interpreter

    private sealed class ExitSignal : Exception
    {
        public ExitSignal(string what) => What = what;

        public string What { get; }
    }

    private sealed class Interpreter : IVariableScope
    {
        private readonly Project _project;
        private readonly Program _program;
        private readonly IReadOnlyList<ProjectTask> _selection;
        private readonly Action<string>? _onMessage;
        private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<ProjectTask> _currentTasks = new();
        private bool _dirty;
        private int _depth;

        public Interpreter(Project project, Program program, IReadOnlyList<ProjectTask> selection, Action<string>? onMessage)
        {
            _project = project;
            _program = program;
            _selection = selection;
            _onMessage = onMessage;
            ProjectScheduler.Schedule(project);
        }

        public List<string> Messages { get; } = new();
        public List<string> Output { get; } = new();
        public int Count { get; private set; }

        public void Finish()
        {
            if (_dirty)
                ProjectScheduler.Schedule(_project);
        }

        public void RunSub(List<Statement> body)
        {
            if (++_depth > 50)
                throw new MacroException(0, "Subs call each other too deeply.");
            try
            {
                RunBlock(body);
            }
            catch (ExitSignal exit) when (exit.What == "sub")
            {
            }
            finally
            {
                _depth--;
            }
        }

        public void RunBlock(List<Statement> statements)
        {
            foreach (var statement in statements)
                Run(statement);
        }

        /// <summary>Counts a loop pass, so even an empty loop cannot run forever.</summary>
        private void Tick(Statement loop)
        {
            if (++Count > MaxStatements)
                throw new MacroException(loop.Line, "The macro ran too long (it may loop forever).");
        }

        private void Run(Statement statement)
        {
            if (++Count > MaxStatements)
                throw new MacroException(statement.Line, "The macro ran too long (it may loop forever).");
            try
            {
                switch (statement)
                {
                    case Assign a:
                        DoAssign(a);
                        break;
                    case ForEach f:
                        DoForEach(f);
                        break;
                    case ForRange f:
                        DoForRange(f);
                        break;
                    case IfBlock i:
                        DoIf(i);
                        break;
                    case DoLoop d:
                        DoLoop(d);
                        break;
                    case Exit e:
                        throw new ExitSignal(e.What);
                    case Command c:
                        DoCommand(c);
                        break;
                }
            }
            catch (FormulaException ex)
            {
                throw new MacroException(statement.Line, ex.Message);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
            {
                throw new MacroException(statement.Line, ex.Message);
            }
        }

        // ---- expressions

        public object? Eval(string expression)
        {
            if (_dirty)
            {
                ProjectScheduler.Schedule(_project);
                _dirty = false;
            }
            var trimmed = expression.Trim();
            var add = Regex.Match(trimmed, @"^(?:ActiveProject\.)?(Tasks|Resources)\.Add\s*\((.*)\)$", RegexOptions.IgnoreCase);
            if (add.Success)
                return AddItem(add.Groups[1].Value, add.Groups[2].Value);
            return Formula.Parse(trimmed, allowVariables: true).Evaluate(_project, _currentTasks.Count > 0 ? _currentTasks.Peek() : null, this);
        }

        private bool Truth(string expression) => Eval(expression) switch
        {
            bool b => b,
            null => false,
            double d => d != 0,
            string s => s.Length > 0 && !s.Equals("False", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };

        private static double Number(object? value) => value switch
        {
            double d => d,
            long l => l,
            int i => i,
            decimal m => (double)m,
            bool b => b ? -1 : 0,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var n) => n,
            _ => throw new FormulaException($"'{value}' is not a number."),
        };

        private static string Text(object? value) => value switch
        {
            null => "",
            bool b => b ? "True" : "False",
            double d => d.ToString("0.##########", CultureInfo.CurrentCulture),
            DateTime d => d.ToString("g", CultureInfo.CurrentCulture),
            ProjectTask t => t.Name,
            Resource r => r.Name,
            _ => value.ToString() ?? "",
        };

        /// <summary>Arguments separated by commas outside quotes and parentheses.</summary>
        private List<object?> Arguments(string text)
        {
            var parts = new List<string>();
            bool quoted = false;
            int depth = 0, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"') quoted = !quoted;
                else if (!quoted && ch is '(' or '[') depth++;
                else if (!quoted && ch is ')' or ']') depth--;
                else if (!quoted && depth == 0 && ch == ',')
                {
                    parts.Add(text[start..i]);
                    start = i + 1;
                }
            }
            if (text.Trim().Length > 0)
                parts.Add(text[start..]);
            return parts.Select(p => Eval(p)).ToList();
        }

        private object AddItem(string kind, string arguments)
        {
            var args = Arguments(arguments);
            var name = args.Count > 0 ? Text(args[0]) : "";
            _dirty = true;
            if (kind.Equals("Resources", StringComparison.OrdinalIgnoreCase))
                return _project.AddResource(name);
            var task = args.Count > 1 && args[1] is not null
                ? _project.InsertTask(Math.Clamp((int)Number(args[1]) - 1, 0, _project.Tasks.Count), name)
                : _project.AddTask(name);
            task.DurationMinutes = _project.Options.MinutesPerDay;
            task.IsEstimated = true;
            return task;
        }

        // ---- IVariableScope

        public bool TryGet(string name, out object? value)
        {
            if (_variables.TryGetValue(name, out value))
                return true;
            switch (name.ToLowerInvariant())
            {
                case "activeproject":
                    value = _project;
                    return true;
                case "tasks":
                    value = _project.Tasks.ToList();
                    return true;
                case "resources":
                    value = _project.Resources.ToList();
                    return true;
                case "selectedtasks":
                case "activeselection":
                    value = _selection.Select(t => _project.GetTask(t.Id)).OfType<ProjectTask>().ToList();
                    return true;
                case "activetask":
                case "activecell":
                    value = _currentTasks.Count > 0 ? _currentTasks.Peek() : _selection.FirstOrDefault();
                    return true;
                case "vbcrlf":
                case "vbnewline":
                    value = Environment.NewLine;
                    return true;
                case "vbtab":
                    value = "\t";
                    return true;
            }
            return false;
        }

        public object? Member(object? target, string member)
        {
            if (_dirty)
            {
                ProjectScheduler.Schedule(_project);
                _dirty = false;
            }
            switch (target)
            {
                case null:
                    throw new FormulaException($"'.{member}' of Nothing.");
                case Project p:
                    return member.ToLowerInvariant() switch
                    {
                        "tasks" => p.Tasks.ToList(),
                        "resources" => p.Resources.ToList(),
                        "name" => p.Name,
                        "start" or "projectstart" => p.Start,
                        "finish" or "projectfinish" => p.Finish,
                        "statusdate" => p.StatusDate,
                        "cost" => (double)p.Cost,
                        "work" => (double)p.WorkMinutes,
                        _ => throw new FormulaException($"A project has no '{member}'."),
                    };
                case System.Collections.ICollection list when member.Equals("Count", StringComparison.OrdinalIgnoreCase):
                    return (double)list.Count;
                case ProjectTask t:
                    return TaskMember(t, member);
                case Resource r:
                    return ResourceMember(r, member);
                case Assignment a:
                    return member.ToLowerInvariant() switch
                    {
                        "task" => a.Task,
                        "resource" => a.Resource,
                        "resourcename" => a.Resource.Name,
                        "taskname" => a.Task.Name,
                        "units" => a.Units,
                        "work" => (double)a.WorkMinutes,
                        "actualwork" => (double)a.ActualWorkMinutes,
                        "cost" => (double)a.Cost,
                        "start" => a.Start,
                        "finish" => a.Finish,
                        _ => throw new FormulaException($"An assignment has no '{member}'."),
                    };
                case string s when member.Equals("Length", StringComparison.OrdinalIgnoreCase):
                    return (double)s.Length;
            }
            throw new FormulaException($"'{Text(target)}' has no '{member}'.");
        }

        private object? TaskMember(ProjectTask t, string member)
        {
            switch (member.ToLowerInvariant())
            {
                case "id": return (double)(_project.IndexOf(t) + 1);
                case "uniqueid": return (double)t.Id;
                case "children": return t.Children.ToList();
                case "parent" or "outlineparent": return t.Parent;
                case "assignments": return t.Assignments.ToList();
                case "summary": return t.IsSummary;
                case "milestone": return t.IsMilestone || !t.IsSummary && t.DurationMinutes == 0;
                case "percentcomplete": return t.PercentComplete;
            }
            var field = FindTaskField(member) ?? throw new FormulaException($"A task has no field '{member}'.");
            return field.Get(_project, t) switch
            {
                decimal m => (double)m,
                long l => (double)l,
                var v => v,
            };
        }

        private FieldDefinition? FindTaskField(string member)
        {
            var compact = member.Replace(" ", "");
            return TaskFields.All(_project).FirstOrDefault(f =>
                f.Key.Equals(member, StringComparison.OrdinalIgnoreCase) || f.Name.Equals(member, StringComparison.OrdinalIgnoreCase)
                || f.Name.Replace(" ", "").Equals(compact, StringComparison.OrdinalIgnoreCase));
        }

        private static object? ResourceMember(Resource r, string member) => member.ToLowerInvariant().Replace(" ", "") switch
        {
            "name" => r.Name,
            "uniqueid" or "id" => (double)r.Id,
            "type" => r.Type.ToString(),
            "initials" => r.Initials,
            "group" => r.Group,
            "code" => r.Code,
            "email" or "emailaddress" => r.Email,
            "notes" => r.Notes,
            "maxunits" => r.MaxUnits,
            "standardrate" => (double)r.StandardRate,
            "overtimerate" => (double)r.OvertimeRate,
            "costperuse" => (double)r.CostPerUse,
            "work" => (double)r.WorkMinutes,
            "cost" => (double)r.Cost,
            "peakunits" or "peak" => r.PeakUnits,
            "overallocated" => r.IsOverallocated,
            "generic" => r.IsGeneric,
            "canlevel" => r.CanLevel,
            _ => throw new FormulaException($"A resource has no '{member}'."),
        };

        // ---- statements

        private void DoAssign(Assign a)
        {
            var value = Eval(a.Expression);
            var target = a.Target;
            if (target.StartsWith('['))
            {
                var task = _currentTasks.Count > 0 ? _currentTasks.Peek() : throw new FormulaException($"{target} needs a current task (inside For Each t In Tasks).");
                SetTaskField(task, target.Trim('[', ']'), value);
                return;
            }
            int dot = target.LastIndexOf('.');
            if (dot < 0)
            {
                if (TryGet(target, out var existing) && !_variables.ContainsKey(target) && existing is not null && !a.IsSet)
                    throw new FormulaException($"'{target}' cannot be changed.");
                _variables[target] = value;
                return;
            }
            var owner = Eval(target[..dot]);
            var member = target[(dot + 1)..].Trim('[', ']');
            switch (owner)
            {
                case ProjectTask t:
                    SetTaskField(t, member, value);
                    break;
                case Resource r:
                    SetResourceField(r, member, value);
                    break;
                case Project p when member.Equals("StatusDate", StringComparison.OrdinalIgnoreCase):
                    p.StatusDate = value as DateTime?;
                    _dirty = true;
                    break;
                case Project p when member.Equals("Name", StringComparison.OrdinalIgnoreCase):
                    p.Name = Text(value);
                    break;
                default:
                    throw new FormulaException($"'{target}' cannot be set.");
            }
        }

        private void SetTaskField(ProjectTask t, string member, object? value)
        {
            _dirty = true;
            var options = _project.Options;
            switch (member.ToLowerInvariant().Replace(" ", ""))
            {
                case "name":
                    t.Name = Text(value);
                    return;
                case "notes":
                    t.Notes = Text(value);
                    return;
                case "duration":
                    if (t.IsSummary)
                        throw new FormulaException("A summary task's duration comes from its subtasks.");
                    if (value is string text)
                    {
                        if (!DurationText.TryParse(text, options, out var d))
                            throw new FormulaException($"'{text}' is not a duration.");
                        _project.SetDuration(t, d.Minutes);
                    }
                    else
                    {
                        _project.SetDuration(t, (long)Math.Round(Number(value)));
                    }
                    return;
                case "start":
                    if (value is not DateTime start)
                        throw new FormulaException("Start must be a date.");
                    if (t.Mode == TaskMode.ManuallyScheduled)
                        t.ManualStart = start;
                    else
                    {
                        t.ConstraintType = ConstraintType.StartNoEarlierThan;
                        t.ConstraintDate = start;
                    }
                    return;
                case "finish":
                    if (value is not DateTime finish)
                        throw new FormulaException("Finish must be a date.");
                    if (t.Mode == TaskMode.ManuallyScheduled)
                        t.ManualFinish = finish;
                    else
                    {
                        t.ConstraintType = ConstraintType.FinishNoEarlierThan;
                        t.ConstraintDate = finish;
                    }
                    return;
                case "deadline":
                    t.Deadline = value as DateTime?;
                    return;
                case "percentcomplete" or "%complete":
                    if (t.IsSummary)
                        throw new FormulaException("Enter progress on subtasks; a summary task rolls it up.");
                    TaskProgress.SetPercentComplete(t, Number(value));
                    return;
                case "priority":
                    t.Priority = (int)Math.Clamp(Math.Round(Number(value)), 0, 1000);
                    return;
                case "marked":
                    t.IsMarked = Formula.Convert(value, FieldType.Flag) is true;
                    return;
                case "milestone":
                    t.IsMilestone = Formula.Convert(value, FieldType.Flag) is true;
                    return;
                case "active":
                    t.IsActive = Formula.Convert(value, FieldType.Flag) is true;
                    return;
                case "hidebar":
                    t.HideBar = Formula.Convert(value, FieldType.Flag) is true;
                    return;
                case "fixedcost":
                    t.FixedCost = (decimal)Number(value);
                    return;
                case "hyperlink":
                    t.Hyperlink = Text(value);
                    return;
                case "hyperlinkaddress":
                    t.HyperlinkAddress = Text(value);
                    return;
                case "resourcenames":
                    ResourceNamesText.Apply(_project, t, Text(value));
                    return;
                case "predecessors":
                    t.Predecessors.Clear();
                    PredecessorText.Apply(_project, t, Text(value));
                    return;
                case "manual":
                    t.Mode = Formula.Convert(value, FieldType.Flag) is true ? TaskMode.ManuallyScheduled : TaskMode.AutoScheduled;
                    return;
            }
            var field = FindTaskField(member);
            if (field is { IsCustom: true } && CustomField.TypeOfKey(field.Key) is { } type)
            {
                if (field.IsCalculated)
                    throw new FormulaException($"{field.Name} is calculated by a formula.");
                var converted = Formula.Convert(value, type);
                if (converted is null)
                    t.CustomValues.Remove(field.Key);
                else
                    t.CustomValues[field.Key] = converted;
                return;
            }
            throw new FormulaException(field is null ? $"A task has no field '{member}'." : $"{field.Name} cannot be set by a macro.");
        }

        private void SetResourceField(Resource r, string member, object? value)
        {
            _dirty = true;
            switch (member.ToLowerInvariant().Replace(" ", ""))
            {
                case "name": r.Name = Text(value); break;
                case "initials": r.Initials = Text(value); break;
                case "group": r.Group = Text(value); break;
                case "code": r.Code = Text(value); break;
                case "email" or "emailaddress": r.Email = Text(value); break;
                case "notes": r.Notes = Text(value); break;
                case "maxunits": r.MaxUnits = Number(value); break;
                case "standardrate": r.StandardRate = (decimal)Number(value); break;
                case "overtimerate": r.OvertimeRate = (decimal)Number(value); break;
                case "costperuse": r.CostPerUse = (decimal)Number(value); break;
                case "generic": r.IsGeneric = Formula.Convert(value, FieldType.Flag) is true; break;
                case "canlevel": r.CanLevel = Formula.Convert(value, FieldType.Flag) is true; break;
                default: throw new FormulaException($"A resource has no settable '{member}'.");
            }
        }

        private void DoForEach(ForEach f)
        {
            if (Eval(f.Collection) is not System.Collections.IEnumerable items || items is string)
                throw new FormulaException($"'{f.Collection}' is not a list (use Tasks, Resources, SelectedTasks, t.Children or t.Assignments).");
            var snapshot = items.Cast<object?>().ToList();
            foreach (var item in snapshot)
            {
                // Skip items a previous iteration deleted.
                if (item is ProjectTask deleted && _project.GetTask(deleted.Id) != deleted)
                    continue;
                _variables[f.Variable] = item;
                if (item is ProjectTask task)
                    _currentTasks.Push(task);
                try
                {
                    RunBlock(f.Body);
                }
                catch (ExitSignal exit) when (exit.What == "for")
                {
                    break;
                }
                finally
                {
                    if (item is ProjectTask)
                        _currentTasks.Pop();
                }
            }
        }

        private void DoForRange(ForRange f)
        {
            double from = Number(Eval(f.From)), to = Number(Eval(f.To)), step = f.Step is null ? 1 : Number(Eval(f.Step));
            if (step == 0)
                throw new FormulaException("A For loop's Step cannot be 0.");
            for (double i = from; step > 0 ? i <= to : i >= to; i += step)
            {
                Tick(f);
                _variables[f.Variable] = i;
                try
                {
                    RunBlock(f.Body);
                }
                catch (ExitSignal exit) when (exit.What == "for")
                {
                    break;
                }
                // The body may change the counter, as in VBA.
                i = Number(_variables[f.Variable]);
            }
        }

        private void DoIf(IfBlock block)
        {
            foreach (var (condition, body) in block.Branches)
            {
                if (Truth(condition))
                {
                    RunBlock(body);
                    return;
                }
            }
            if (block.Else is { } otherwise)
                RunBlock(otherwise);
        }

        private void DoLoop(DoLoop loop)
        {
            bool Continue() => loop.Condition is null || Truth(loop.Condition) != loop.Until;
            try
            {
                if (loop.TestAtEnd)
                {
                    do
                    {
                        Tick(loop);
                        RunBlock(loop.Body);
                    }
                    while (Continue());
                }
                else
                {
                    while (Continue())
                    {
                        Tick(loop);
                        RunBlock(loop.Body);
                    }
                }
            }
            catch (ExitSignal exit) when (exit.What == "do")
            {
            }
        }

        private void DoCommand(Command c)
        {
            switch (c.Name)
            {
                case "dim":
                    foreach (var part in c.Arguments.Split(','))
                    {
                        var declared = Regex.Match(part.Trim(), @"^([A-Za-z_]\w*)").Groups[1].Value;
                        if (declared.Length > 0 && !_variables.ContainsKey(declared))
                            _variables[declared] = null;
                    }
                    break;
                case "msgbox":
                    var message = Text(Arguments(c.Arguments).FirstOrDefault());
                    Messages.Add(message);
                    _onMessage?.Invoke(message);
                    break;
                case "debug.print" or "print":
                    Output.Add(string.Join(" ", Arguments(c.Arguments).Select(Text)));
                    break;
                case "calculateproject":
                    ProjectScheduler.Schedule(_project);
                    _dirty = false;
                    break;
                case "call":
                    var name = Regex.Match(c.Arguments, @"^([A-Za-z_]\w*)").Groups[1].Value;
                    if (!_program.Subs.TryGetValue(name, out var body))
                        throw new FormulaException($"There is no Sub named '{name}'.");
                    RunSub(body);
                    break;
                case "delete":
                    switch (Eval(c.Arguments))
                    {
                        case ProjectTask t:
                            _project.RemoveTask(t);
                            break;
                        case Resource r:
                            _project.RemoveResource(r);
                            break;
                        default:
                            throw new FormulaException($"'{c.Arguments}' is not a task or resource.");
                    }
                    _dirty = true;
                    break;
                case "linktasks" or "unlinktasks":
                {
                    var args = Arguments(c.Arguments);
                    if (args.Count < 2 || args[0] is not ProjectTask from || args[1] is not ProjectTask to)
                        throw new FormulaException($"{c.Name} needs two tasks.");
                    if (c.Name == "unlinktasks")
                        _project.Unlink(from, to);
                    else
                    {
                        var type = (args.Count > 2 ? Text(args[2]) : "FS").ToUpperInvariant() switch
                        {
                            "SS" => LinkType.StartToStart,
                            "FF" => LinkType.FinishToFinish,
                            "SF" => LinkType.StartToFinish,
                            _ => LinkType.FinishToStart,
                        };
                        _project.Link(from, to, type, args.Count > 3 ? (long)Number(args[3]) : 0);
                    }
                    _dirty = true;
                    break;
                }
                case "assignresource":
                {
                    var args = Arguments(c.Arguments);
                    if (args.Count < 2 || args[0] is not ProjectTask task)
                        throw new FormulaException("AssignResource needs a task and a resource.");
                    var resource = args[1] as Resource ?? _project.Resources.FirstOrDefault(r => r.Name.Equals(Text(args[1]), StringComparison.OrdinalIgnoreCase))
                                   ?? _project.AddResource(Text(args[1]));
                    _project.Assign(task, resource, args.Count > 2 ? Number(args[2]) : 1.0);
                    _dirty = true;
                    break;
                }
                case "indent" or "outdent":
                {
                    if (Arguments(c.Arguments).FirstOrDefault() is not ProjectTask task)
                        throw new FormulaException($"{c.Name} needs a task.");
                    if (c.Name == "indent")
                        _project.Indent(task);
                    else
                        _project.Outdent(task);
                    _dirty = true;
                    break;
                }
            }
        }
    }
}
