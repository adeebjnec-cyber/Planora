using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;
using Planora.Engine.Views;

namespace Planora.Engine.Interop;

/// <summary>
/// Excel (.xlsx) and CSV import/export of task tables.
/// <para>
/// Export writes the given table's columns for the given tasks (a header row, then one row per task).
/// Import reads a sheet whose header row names fields (by field name, e.g. "Task Name", "Duration",
/// "Start", "Predecessors", "Outline Level", "Resource Names", or a custom field's alias), and builds
/// a project from it, as Microsoft Project's import wizard does with a field mapping.
/// </para>
/// </summary>
public static class SpreadsheetFile
{
    // ------------------------------------------------------------------ export

    /// <summary>Header plus one row of display text per task.</summary>
    public static List<string[]> Rows(Project project, TableDefinition table, IEnumerable<ProjectTask> tasks)
    {
        var fields = table.Columns.Select(c => (Column: c, Field: TaskFields.Find(project, c.FieldKey)))
            .Where(x => x.Field is not null).ToList();
        var rows = new List<string[]>
        {
            new[] { "ID", "Outline Level" }.Concat(fields.Select(x => x.Column.Title ?? x.Field!.Name)).ToArray(),
        };
        foreach (var task in tasks)
        {
            rows.Add(new[] { (project.IndexOf(task) + 1).ToString(CultureInfo.InvariantCulture), task.OutlineLevel.ToString(CultureInfo.InvariantCulture) }
                .Concat(fields.Select(x => x.Field!.Get(project, task) is DateTime d ? d.ToString("g", CultureInfo.CurrentCulture) : x.Field!.Format(project, task)))
                .ToArray());
        }
        return rows;
    }

    public static void SaveCsv(IEnumerable<string[]> rows, string path)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Quote)));
        // UTF-8 with BOM so Excel detects the encoding.
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        static string Quote(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    /// <summary>Writes a single-sheet workbook. Numbers are stored as numbers; the header row is bold.</summary>
    public static void SaveXlsx(IReadOnlyList<string[]> rows, string path, string sheetName = "Tasks")
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        void Part(string name, string content)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        Part("[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);
        Part("_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        Part("xl/workbook.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="{SecurityElement.Escape(sheetName)}" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        Part("xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        Part("xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
              <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
              <borders count="1"><border/></borders>
              <cellStyleXfs count="1"><xf/></cellStyleXfs>
              <cellXfs count="2"><xf fontId="0"/><xf fontId="1" applyFont="1"/></cellXfs>
            </styleSheet>
            """);

        var sheet = new StringBuilder();
        sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (int r = 0; r < rows.Count; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (int c = 0; c < rows[r].Length; c++)
            {
                var reference = ColumnName(c) + (r + 1);
                var value = rows[r][c];
                var style = r == 0 ? " s=\"1\"" : "";
                if (r > 0 && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    sheet.Append($"<c r=\"{reference}\"{style}><v>{number.ToString(CultureInfo.InvariantCulture)}</v></c>");
                else
                    sheet.Append($"<c r=\"{reference}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></is></c>");
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");
        Part("xl/worksheets/sheet1.xml", sheet.ToString());
    }

    private static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26)
            name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    // ------------------------------------------------------------------ import

    public static List<string[]> LoadCsv(string path)
    {
        var text = File.ReadAllText(path);
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                    quoted = false;
                else
                    cell.Append(ch);
            }
            else if (ch == '"')
                quoted = true;
            else if (ch == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row.ToArray());
                row.Clear();
            }
            else
                cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row.ToArray());
        }
        return rows.Where(r => r.Any(c => c.Length > 0)).ToList();
    }

    /// <summary>Reads the first worksheet of an .xlsx file as text cells (numbers as invariant text, Excel dates converted).</summary>
    public static List<string[]> LoadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        XDocument Load(string name) => XDocument.Load((zip.GetEntry(name) ?? throw new InvalidDataException($"The workbook has no {name}.")).Open());

        var shared = zip.GetEntry("xl/sharedStrings.xml") is { } sharedEntry
            ? XDocument.Load(sharedEntry.Open()).Root!.Elements(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToList()
            : new List<string>();

        // Cell styles whose number format is a date (built-in formats 14-22, or a custom one with d/m/y).
        var dateStyles = new HashSet<int>();
        if (zip.GetEntry("xl/styles.xml") is { } stylesEntry)
        {
            var styles = XDocument.Load(stylesEntry.Open()).Root!;
            var customDateFormats = styles.Element(ns + "numFmts")?.Elements(ns + "numFmt")
                .Where(f => ((string?)f.Attribute("formatCode") ?? "").ToLowerInvariant().IndexOfAny(new[] { 'd', 'y' }) >= 0)
                .Select(f => (int)f.Attribute("numFmtId")!).ToHashSet() ?? new HashSet<int>();
            var xfs = styles.Element(ns + "cellXfs")?.Elements(ns + "xf").ToList() ?? new List<XElement>();
            for (int i = 0; i < xfs.Count; i++)
            {
                int format = (int?)xfs[i].Attribute("numFmtId") ?? 0;
                if (format is >= 14 and <= 22 || customDateFormats.Contains(format))
                    dateStyles.Add(i);
            }
        }

        // First sheet's part, via the workbook relationships.
        var workbook = Load("xl/workbook.xml");
        var firstSheet = workbook.Root!.Element(ns + "sheets")?.Elements(ns + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("The workbook has no worksheets.");
        var relId = (string?)firstSheet.Attribute(rel + "id");
        var target = Load("xl/_rels/workbook.xml.rels").Root!.Elements()
            .FirstOrDefault(r => (string?)r.Attribute("Id") == relId)?.Attribute("Target")?.Value ?? "worksheets/sheet1.xml";
        var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        var rows = new List<string[]>();
        foreach (var row in Load(sheetPath).Root!.Descendants(ns + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            int next = 0;
            foreach (var c in row.Elements(ns + "c"))
            {
                int column = (string?)c.Attribute("r") is { } reference ? ColumnIndex(reference) : next;
                next = column + 1;
                var type = (string?)c.Attribute("t");
                var raw = c.Element(ns + "v")?.Value;
                string value = type switch
                {
                    "s" when int.TryParse(raw, out var index) && index < shared.Count => shared[index],
                    "inlineStr" => string.Concat(c.Descendants(ns + "t").Select(t => t.Value)),
                    "b" => raw == "1" ? "Yes" : "No",
                    _ => raw ?? "",
                };
                if (type is null or "n" && (int?)c.Attribute("s") is { } style && dateStyles.Contains(style) &&
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
                    value = DateTime.FromOADate(serial).ToString("g", CultureInfo.CurrentCulture);
                cells[column] = value;
            }
            if (cells.Count > 0)
            {
                var values = new string[cells.Keys.Max() + 1];
                for (int i = 0; i < values.Length; i++)
                    values[i] = cells.TryGetValue(i, out var v) ? v : "";
                rows.Add(values);
            }
        }
        return rows;
    }

    private static int ColumnIndex(string reference)
    {
        int index = 0;
        foreach (char ch in reference.TakeWhile(char.IsLetter))
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return index - 1;
    }

    /// <summary>
    /// Builds a project from rows whose first row names the columns. Recognised columns: Task Name/Name,
    /// Duration, Start, Finish, Predecessors (row numbers within the sheet), Outline Level, Resource Names,
    /// % Complete, Notes, Milestone, Deadline, Priority, Fixed Cost, and custom fields by slot or alias.
    /// A start date on a task with no predecessors becomes a Start No Earlier Than constraint.
    /// </summary>
    public static Project Import(IReadOnlyList<string[]> rows, string name, DateTime? projectStart = null)
    {
        if (rows.Count == 0)
            throw new InvalidDataException("The file is empty.");
        var header = rows[0].Select(h => h.Trim()).ToArray();
        int Col(params string[] names) => Array.FindIndex(header, h => names.Any(n => string.Equals(h, n, StringComparison.OrdinalIgnoreCase)));

        int nameCol = Col("Task Name", "Name", "Task");
        if (nameCol < 0)
            throw new InvalidDataException("The first row must name the columns, including a 'Task Name' or 'Name' column.");
        int durationCol = Col("Duration"), startCol = Col("Start"), finishCol = Col("Finish"), predCol = Col("Predecessors");
        int levelCol = Col("Outline Level", "Level"), resourceCol = Col("Resource Names", "Resources"), percentCol = Col("% Complete", "Percent Complete");
        int notesCol = Col("Notes"), milestoneCol = Col("Milestone"), deadlineCol = Col("Deadline"), priorityCol = Col("Priority"), fixedCostCol = Col("Fixed Cost");

        var data = rows.Skip(1).Where(r => nameCol < r.Length && !string.IsNullOrWhiteSpace(r[nameCol])).ToList();
        string Cell(string[] row, int col) => col >= 0 && col < row.Length ? row[col].Trim() : "";

        // Earliest start in the sheet (or today) becomes the project start.
        var starts = data.Select(r => DateTime.TryParse(Cell(r, startCol), CultureInfo.CurrentCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
            .OfType<DateTime>().ToList();
        var project = new Project(name, projectStart ?? (starts.Count > 0 ? starts.Min() : DateTime.Today.AddHours(8)));
        var o = project.Options;

        var customColumns = new List<(int Col, string Key)>();
        for (int c = 0; c < header.Length; c++)
        {
            if (CustomField.TypeOfKey(header[c]) is not null)
                customColumns.Add((c, header[c]));
        }

        var tasks = new List<ProjectTask>();
        int previousLevel = 0;
        foreach (var row in data)
        {
            var task = project.AddTask(Cell(row, nameCol));
            int level = int.TryParse(Cell(row, levelCol), out var l) ? l : 1;
            level = Math.Clamp(level, 1, previousLevel + 1);
            for (int i = 1; i < level; i++)
                project.Indent(task);
            previousLevel = level;

            if (DurationText.TryParse(Cell(row, durationCol), o, out var d))
            {
                task.DurationMinutes = d.Minutes;
                task.IsElapsedDuration = d.IsElapsed;
                task.IsEstimated = d.IsEstimated;
            }
            else if (durationCol < 0 && DateTime.TryParse(Cell(row, startCol), CultureInfo.CurrentCulture, DateTimeStyles.None, out var s) &&
                     DateTime.TryParse(Cell(row, finishCol), CultureInfo.CurrentCulture, DateTimeStyles.None, out var f))
            {
                var cal = project.Calendar;
                task.DurationMinutes = Math.Max(0, cal.WorkingMinutesBetween(cal.NextWorkingMoment(s), f.TimeOfDay == TimeSpan.Zero ? cal.PreviousWorkingMoment(f.AddDays(1)) : f));
            }
            else
            {
                task.DurationMinutes = o.MinutesPerDay;
                task.IsEstimated = true;
            }

            if (double.TryParse(Cell(row, percentCol).TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var pct))
                task.PercentComplete = Math.Clamp(pct, 0, 100);
            task.Notes = Cell(row, notesCol);
            task.IsMilestone = Cell(row, milestoneCol).ToLowerInvariant() is "yes" or "true" or "1";
            if (DateTime.TryParse(Cell(row, deadlineCol), CultureInfo.CurrentCulture, DateTimeStyles.None, out var deadline))
                task.Deadline = deadline.TimeOfDay == TimeSpan.Zero ? project.Calendar.PreviousWorkingMoment(deadline.AddDays(1)) : deadline;
            if (int.TryParse(Cell(row, priorityCol), out var priority))
                task.Priority = Math.Clamp(priority, 0, 1000);
            if (decimal.TryParse(Cell(row, fixedCostCol), NumberStyles.Currency, CultureInfo.CurrentCulture, out var fixedCost))
                task.FixedCost = fixedCost;
            foreach (var (col, key) in customColumns)
                TaskFields.TrySetCustom(project, task, key, Cell(row, col));
            tasks.Add(task);
        }

        // Summaries get their dates from their subtasks.
        foreach (var t in tasks.Where(t => t.IsSummary))
            t.DurationMinutes = 0;

        for (int i = 0; i < data.Count; i++)
        {
            var task = tasks[i];
            var predecessors = Cell(data[i], predCol);
            if (predecessors.Length > 0)
            {
                try
                {
                    PredecessorText.Apply(project, task, predecessors);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                {
                    task.Notes = (task.Notes + $"\nImport: predecessors '{predecessors}' could not be applied ({ex.Message}).").Trim();
                }
            }
            if (!task.IsSummary && task.Predecessors.Count == 0 &&
                DateTime.TryParse(Cell(data[i], startCol), CultureInfo.CurrentCulture, DateTimeStyles.None, out var start) && start.Date > project.StartDate.Date)
            {
                task.ConstraintType = ConstraintType.StartNoEarlierThan;
                task.ConstraintDate = project.Calendar.NextWorkingMoment(start);
            }
            if (!task.IsSummary && Cell(data[i], resourceCol) is { Length: > 0 } resources)
            {
                try
                {
                    ResourceNamesText.Apply(project, task, resources);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                {
                    task.Notes = (task.Notes + $"\nImport: resources '{resources}' could not be applied ({ex.Message}).").Trim();
                }
            }
        }

        ProjectScheduler.Schedule(project);
        foreach (var task in tasks.Where(t => t.PercentComplete > 0 && !t.IsSummary))
        {
            task.ActualStart = task.Start;
            if (task.PercentComplete >= 100)
                task.ActualFinish = task.Finish;
        }
        return project;
    }
}
