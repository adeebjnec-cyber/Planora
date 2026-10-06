using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Planora.Engine.Calendars;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Interop;

/// <summary>
/// Reads and writes Microsoft Project XML (MSPDI, "Save As > XML" in Microsoft Project): project
/// settings, calendars with exceptions, tasks (hierarchy, links, constraints, progress, baselines),
/// resources with rate tables, and assignments.
/// <para>
/// Conventions of the format: durations are ISO 8601 (PT8H0M0S) in working time, link lags and
/// leveling delays are in tenths of a minute (tenths of a percent for percentage lags), costs are in
/// cents, and rates are per hour. Codes for constraint types, contours and task types match
/// Microsoft Project's numbering.
/// </para>
/// </summary>
public static class MspdiFile
{
    public const string Extension = ".xml";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/project";

    public static void Save(Project project, string path) => Write(project).Save(path);

    public static Project Load(string path)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(path);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"The file is not valid XML: {ex.Message}", ex);
        }
        return Read(doc);
    }

    // ------------------------------------------------------------------ writing

    public static XDocument Write(Project project)
    {
        ProjectScheduler.Schedule(project);
        var calendarUid = new Dictionary<WorkingCalendar, int>();
        var calendars = new List<WorkingCalendar>();
        void AddCalendar(WorkingCalendar? c)
        {
            if (c is null || calendarUid.ContainsKey(c))
                return;
            AddCalendar(c.BaseCalendar);
            calendarUid[c] = calendars.Count + 1;
            calendars.Add(c);
        }
        AddCalendar(project.Calendar);
        project.Calendars.ForEach(AddCalendar);
        foreach (var r in project.Resources)
            AddCalendar(r.Calendar);
        foreach (var t in project.Tasks)
            AddCalendar(t.Calendar);

        var o = project.Options;
        var root = new XElement(Ns + "Project",
            E("SaveVersion", 14),
            E("Name", project.Name + ".xml"),
            E("Title", project.Name),
            E("ScheduleFromStart", !project.ScheduleFromFinish),
            E("StartDate", project.ScheduleFromFinish ? project.Start : project.StartDate),
            E("FinishDate", project.ScheduleFromFinish ? project.FinishDate : project.Finish),
            E("CalendarUID", calendarUid[project.Calendar]),
            E("MinutesPerDay", o.MinutesPerDay),
            E("MinutesPerWeek", o.MinutesPerWeek),
            E("DaysPerMonth", o.DaysPerMonth),
            E("CriticalSlackLimit", (int)(o.CriticalSlackLimitMinutes / Math.Max(1, o.MinutesPerDay))),
            E("MultipleCriticalPaths", o.CalculateMultipleCriticalPaths),
            E("MoveRemainingPartsBackToStatusDate", o.RescheduleUncompletedWorkToStatusDate),
            project.StatusDate is { } status ? E("StatusDate", status) : null,
            E("DurationFormat", 7),
            new XElement(Ns + "Calendars", calendars.Select(c => WriteCalendar(c, calendarUid, project))),
            new XElement(Ns + "Tasks", project.Tasks.Select(t => WriteTask(project, t, calendarUid))),
            new XElement(Ns + "Resources", project.Resources.Select(r => WriteResource(r, calendarUid))),
            new XElement(Ns + "Assignments", project.Assignments.Select((a, i) => WriteAssignment(a, i + 1))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
    }

    private static XElement WriteCalendar(WorkingCalendar c, Dictionary<WorkingCalendar, int> uid, Project project)
    {
        bool isBase = c.BaseCalendar is null;
        var weekDays = new XElement(Ns + "WeekDays");
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            if (!isBase && c.IsWorkWeekDayInherited(day))
                continue;
            var times = c.GetWorkWeekDay(day);
            weekDays.Add(new XElement(Ns + "WeekDay",
                E("DayType", (int)day + 1),
                E("DayWorking", times.Count > 0),
                times.Count > 0 ? WorkingTimes(times) : null));
        }
        return new XElement(Ns + "Calendar",
            E("UID", uid[c]),
            E("Name", c.Name),
            E("IsBaseCalendar", isBase),
            E("BaseCalendarUID", c.BaseCalendar is { } b ? uid[b] : -1),
            weekDays,
            new XElement(Ns + "Exceptions", ExpandYearly(c.Exceptions).Select(e => new XElement(Ns + "Exception",
                E("EnteredByOccurrences", false),
                new XElement(Ns + "TimePeriod",
                    E("FromDate", e.From.ToDateTime(TimeOnly.MinValue)),
                    E("ToDate", e.To.ToDateTime(new TimeOnly(23, 59)))),
                E("Occurrences", 1),
                E("Name", e.Name),
                E("Type", 1),
                E("DayWorking", e.IsWorking),
                e.IsWorking ? WorkingTimes(e.WorkingTimes) : null))));
    }

    /// <summary>Yearly exceptions are written as one exception per year for 20 years.</summary>
    private static IEnumerable<CalendarException> ExpandYearly(IEnumerable<CalendarException> exceptions)
    {
        foreach (var e in exceptions)
        {
            if (!e.RepeatYearly)
            {
                yield return e;
                continue;
            }
            for (int years = 0; years < 20; years++)
                yield return new CalendarException(e.Name, e.From.AddYears(years), e.To.AddYears(years), e.WorkingTimes);
        }
    }
    private static XElement WorkingTimes(IEnumerable<WorkingTime> times) =>
        new(Ns + "WorkingTimes", times.Select(t => new XElement(Ns + "WorkingTime",
            E("FromTime", Clock(t.From)),
            E("ToTime", Clock(t.To)))));

    private static string Clock(TimeSpan time) => time >= TimeSpan.FromDays(1) ? "00:00:00" : time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static XElement WriteTask(Project p, ProjectTask t, Dictionary<WorkingCalendar, int> calendarUid)
    {
        var o = p.Options;
        var task = new XElement(Ns + "Task",
            E("UID", t.Id),
            E("ID", p.IndexOf(t) + 1),
            E("Name", t.Name),
            E("Active", t.IsActive),
            E("Manual", t.Mode == TaskMode.ManuallyScheduled),
            E("Type", (int)t.Type),
            E("IsNull", false),
            E("WBS", t.WbsCode),
            E("OutlineNumber", t.WbsCode),
            E("OutlineLevel", t.OutlineLevel),
            E("Priority", t.Priority),
            E("Start", t.Start),
            E("Finish", t.Finish),
            E("Duration", Duration(t.DurationMinutes)),
            E("DurationFormat", DurationFormat(t.IsElapsedDuration, t.IsEstimated)),
            E("Work", Duration(t.WorkMinutes)),
            E("EffortDriven", t.IsEffortDriven),
            E("Recurring", t.IsRecurring),
            E("Estimated", t.IsEstimated),
            E("Milestone", t.IsMilestone || !t.IsSummary && t.DurationMinutes == 0),
            E("Summary", t.IsSummary),
            E("Critical", t.IsCritical),
            E("EarlyStart", t.EarlyStart),
            E("EarlyFinish", t.EarlyFinish),
            E("LateStart", t.LateStart),
            E("LateFinish", t.LateFinish),
            E("TotalSlack", t.TotalFloatMinutes * 10),
            E("FreeSlack", t.FreeFloatMinutes * 10),
            E("Cost", Cents(t.Cost)),
            E("FixedCost", Cents(t.FixedCost)),
            E("FixedCostAccrual", Accrual(t.FixedCostAccrual)),
            E("PercentComplete", (int)Math.Round(t.PercentComplete)),
            t.ActualStart is { } actualStart ? E("ActualStart", actualStart) : null,
            t.ActualFinish is { } actualFinish ? E("ActualFinish", actualFinish) : null,
            E("ActualDuration", Duration(t.ActualDurationMinutes)),
            E("RemainingDuration", Duration(t.RemainingDurationMinutes)),
            E("ConstraintType", (int)t.ConstraintType),
            t.ConstraintDate is { } constraintDate ? E("ConstraintDate", constraintDate) : null,
            t.Deadline is { } deadline ? E("Deadline", deadline) : null,
            E("CalendarUID", t.Calendar is { } cal ? calendarUid[cal] : -1),
            E("IgnoreResourceCalendar", t.IgnoreResourceCalendar),
            E("LevelingDelay", t.LevelingDelayMinutes * 10),
            E("LevelingDelayFormat", 8),
            E("HideBar", false),
            string.IsNullOrEmpty(t.Notes) ? null : E("Notes", t.Notes));

        if (t.Mode == TaskMode.ManuallyScheduled)
        {
            task.Add(E("ManualStart", t.ManualStart ?? t.Start));
            task.Add(E("ManualFinish", t.ManualFinish ?? t.Finish));
        }

        foreach (var link in t.Predecessors)
        {
            bool percent = link.LagFormat == LagFormat.PercentOfPredecessor;
            task.Add(new XElement(Ns + "PredecessorLink",
                E("PredecessorUID", link.PredecessorId),
                E("Type", link.Type switch
                {
                    LinkType.FinishToFinish => 0,
                    LinkType.StartToFinish => 2,
                    LinkType.StartToStart => 3,
                    _ => 1,
                }),
                E("CrossProject", false),
                E("LinkLag", percent ? (long)Math.Round(link.LagPercent * 10) : link.LagMinutes * 10),
                E("LagFormat", percent ? 19 : DurationFormat(link.LagFormat == LagFormat.ElapsedTime, false))));
        }

        for (int i = 0; i < Model.Baseline.Count; i++)
        {
            if (t.Baselines[i] is not { } b)
                continue;
            task.Add(new XElement(Ns + "Baseline",
                E("Number", i),
                E("Start", b.Start),
                E("Finish", b.Finish),
                E("Duration", Duration(b.DurationMinutes)),
                E("DurationFormat", 7),
                E("Work", Duration(b.WorkMinutes)),
                E("Cost", Cents(b.Cost))));
        }
        return task;
    }

    private static XElement WriteResource(Resource r, Dictionary<WorkingCalendar, int> calendarUid)
    {
        var resource = new XElement(Ns + "Resource",
            E("UID", r.Id),
            E("ID", r.Id),
            E("Name", r.Name),
            E("Type", r.Type switch { ResourceType.Material => 0, ResourceType.Cost => 2, _ => 1 }),
            E("IsNull", false),
            E("Initials", r.Initials),
            E("Group", r.Group),
            E("Code", r.Code),
            E("EmailAddress", r.Email),
            E("MaterialLabel", r.MaterialLabel),
            E("MaxUnits", r.MaxUnits),
            E("StandardRate", r.StandardRate),
            E("StandardRateFormat", r.Type == ResourceType.Material ? 7 : 2),
            E("OvertimeRate", r.OvertimeRate),
            E("OvertimeRateFormat", 2),
            E("CostPerUse", Cents(r.CostPerUse)),
            E("AccrueAt", Accrual(r.AccrueAt)),
            E("CalendarUID", r.Calendar is { } cal ? calendarUid[cal] : -1),
            E("IsBudget", r.IsBudget),
            string.IsNullOrEmpty(r.Notes) ? null : E("Notes", r.Notes));

        var rates = new XElement(Ns + "Rates");
        for (int table = 0; table < Resource.RateTableCount; table++)
        {
            var rows = r.CostRateTables[table];
            for (int i = 0; i < rows.Count; i++)
            {
                var from = rows[i].EffectiveFrom == DateTime.MinValue ? new DateTime(1984, 1, 1) : rows[i].EffectiveFrom;
                var to = i + 1 < rows.Count ? rows[i + 1].EffectiveFrom.AddMinutes(-1) : new DateTime(2049, 12, 31, 23, 59, 0);
                rates.Add(new XElement(Ns + "Rate",
                    E("RatesFrom", from),
                    E("RatesTo", to),
                    E("RateTable", table),
                    E("StandardRate", rows[i].StandardRate),
                    E("StandardRateFormat", 2),
                    E("OvertimeRate", rows[i].OvertimeRate),
                    E("OvertimeRateFormat", 2),
                    E("CostPerUse", Cents(rows[i].CostPerUse))));
            }
        }
        resource.Add(rates);
        return resource;
    }

    private static XElement WriteAssignment(Assignment a, int uid)
    {
        var assignment = new XElement(Ns + "Assignment",
            E("UID", uid),
            E("TaskUID", a.Task.Id),
            E("ResourceUID", a.Resource.Id),
            E("Units", a.Units),
            E("Work", Duration(a.WorkMinutes)),
            E("ActualWork", Duration(a.ActualWorkMinutes)),
            E("RemainingWork", Duration(a.RemainingWorkMinutes)),
            E("Cost", Cents(a.Resource.Type == ResourceType.Cost ? a.CostAmount : a.Cost)),
            E("Start", a.Start),
            E("Finish", a.Finish),
            E("Delay", a.DelayMinutes * 10),
            E("WorkContour", (int)a.Contour),
            E("CostRateTable", a.CostRateTable));
        for (int i = 0; i < Model.Baseline.Count; i++)
        {
            if (a.Baselines[i] is { } b)
            {
                assignment.Add(new XElement(Ns + "Baseline",
                    E("Number", i), E("Start", b.Start), E("Finish", b.Finish),
                    E("Work", Duration(b.WorkMinutes)), E("Cost", Cents(b.Cost))));
            }
        }
        return assignment;
    }

    private static XElement E(string name, object value) => new(Ns + name, value switch
    {
        bool b => b ? "1" : "0",
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value,
    });

    private static string Duration(long minutes)
    {
        var sign = minutes < 0 ? "-" : "";
        minutes = Math.Abs(minutes);
        return $"{sign}PT{minutes / 60}H{minutes % 60}M0S";
    }

    private static long Cents(decimal amount) => (long)Math.Round(amount * 100);

    /// <summary>MSPDI duration format codes: 7 days, 8 elapsed days; +32 when estimated.</summary>
    private static int DurationFormat(bool elapsed, bool estimated) => (elapsed ? 8 : 7) + (estimated ? 32 : 0);

    private static int Accrual(CostAccrual accrual) => accrual switch
    {
        CostAccrual.Start => 1,
        CostAccrual.End => 2,
        _ => 3,
    };

    // ------------------------------------------------------------------ reading

    public static Project Read(XDocument doc)
    {
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "Project")
            throw new InvalidDataException("This is not a Microsoft Project XML file (no <Project> root).");

        var start = Date(root, "StartDate") ?? DateTime.Today.AddHours(8);
        var project = new Project(Text(root, "Title") ?? Path.GetFileNameWithoutExtension(Text(root, "Name") ?? "Imported"), start)
        {
            ScheduleFromFinish = Text(root, "ScheduleFromStart") == "0",
            FinishDate = Date(root, "FinishDate") ?? start,
            StatusDate = Date(root, "StatusDate"),
        };
        var o = project.Options;
        o.MinutesPerDay = Int(root, "MinutesPerDay") ?? 480;
        o.MinutesPerWeek = Int(root, "MinutesPerWeek") ?? 2400;
        o.DaysPerMonth = Int(root, "DaysPerMonth") ?? 20;
        o.CriticalSlackLimitMinutes = (Int(root, "CriticalSlackLimit") ?? 0) * (long)o.MinutesPerDay;
        o.CalculateMultipleCriticalPaths = Text(root, "MultipleCriticalPaths") == "1";
        o.RescheduleUncompletedWorkToStatusDate = Text(root, "MoveRemainingPartsBackToStatusDate") == "1";

        // Calendars: base calendars first, so derived (resource) calendars can refer to them.
        var calendarElements = Children(root, "Calendars", "Calendar").ToList();
        var calendars = new Dictionary<int, WorkingCalendar>();
        var pending = new Queue<XElement>(calendarElements.OrderBy(c => Int(c, "BaseCalendarUID") is null or -1 ? 0 : 1));
        int guard = pending.Count * pending.Count + 1;
        while (pending.Count > 0 && guard-- > 0)
        {
            var c = pending.Dequeue();
            int baseUid = Int(c, "BaseCalendarUID") ?? -1;
            if (baseUid != -1 && !calendars.ContainsKey(baseUid))
            {
                pending.Enqueue(c);
                continue;
            }
            calendars[Int(c, "UID") ?? 0] = ReadCalendar(c, baseUid == -1 ? null : calendars[baseUid]);
        }
        project.Calendars.Clear();
        foreach (var c in calendarElements.Where(c => Text(c, "IsBaseCalendar") != "0"))
        {
            if (calendars.TryGetValue(Int(c, "UID") ?? 0, out var cal) && cal.BaseCalendar is null)
                project.Calendars.Add(cal);
        }
        if (Int(root, "CalendarUID") is { } projectCalendar && calendars.TryGetValue(projectCalendar, out var pc))
            project.Calendar = pc;
        else if (project.Calendars.Count > 0)
            project.Calendar = project.Calendars[0];
        if (!project.Calendars.Contains(project.Calendar))
            project.Calendars.Insert(0, project.Calendar);

        // Resources.
        foreach (var r in Children(root, "Resources", "Resource"))
        {
            int uid = Int(r, "UID") ?? 0;
            if (uid == 0 || Text(r, "IsNull") == "1" || string.IsNullOrEmpty(Text(r, "Name")))
                continue;
            var type = (Int(r, "Type") ?? 1) switch { 0 => ResourceType.Material, 2 => ResourceType.Cost, _ => ResourceType.Work };
            WorkingCalendar? calendar = null;
            if (type == ResourceType.Work)
            {
                calendar = Int(r, "CalendarUID") is { } cuid && calendars.TryGetValue(cuid, out var rc)
                    ? rc
                    : new WorkingCalendar(Text(r, "Name")!, project.Calendar);
            }
            var resource = project.RestoreResource(uid, Text(r, "Name")!, type, calendar);
            resource.Initials = Text(r, "Initials") ?? "";
            resource.Group = Text(r, "Group") ?? "";
            resource.Code = Text(r, "Code") ?? "";
            resource.Email = Text(r, "EmailAddress") ?? "";
            resource.MaterialLabel = Text(r, "MaterialLabel") ?? "";
            resource.MaxUnits = Double(r, "MaxUnits") ?? 1.0;
            resource.AccrueAt = ReadAccrual(Int(r, "AccrueAt"));
            resource.IsBudget = Text(r, "IsBudget") == "1";
            resource.Notes = Text(r, "Notes") ?? "";
            resource.StandardRate = Money(r, "StandardRate") ?? 0;
            resource.OvertimeRate = Money(r, "OvertimeRate") ?? 0;
            resource.CostPerUse = Cents(r, "CostPerUse") ?? 0;

            foreach (var group in Children(r, "Rates", "Rate").GroupBy(x => Int(x, "RateTable") ?? 0))
            {
                if (group.Key is < 0 or >= Resource.RateTableCount)
                    continue;
                var rows = group.OrderBy(x => Date(x, "RatesFrom") ?? DateTime.MinValue).ToList();
                var table = resource.CostRateTables[group.Key];
                table.Clear();
                for (int i = 0; i < rows.Count; i++)
                {
                    table.Add(new CostRate(
                        i == 0 ? DateTime.MinValue : Date(rows[i], "RatesFrom") ?? DateTime.MinValue,
                        Money(rows[i], "StandardRate") ?? 0,
                        Money(rows[i], "OvertimeRate") ?? 0,
                        Cents(rows[i], "CostPerUse") ?? 0));
                }
            }
        }

        // Tasks (skipping the project summary task UID 0 and blank rows).
        var links = new List<(ProjectTask Task, XElement Link)>();
        var summaries = new HashSet<ProjectTask>();
        foreach (var t in Children(root, "Tasks", "Task").OrderBy(t => Int(t, "ID") ?? 0))
        {
            int uid = Int(t, "UID") ?? 0;
            if (uid == 0 || Text(t, "IsNull") == "1")
                continue;
            var task = project.RestoreTask(uid, Text(t, "Name") ?? "", Math.Max(1, Int(t, "OutlineLevel") ?? 1));
            if (Text(t, "Summary") == "1")
                summaries.Add(task);
            task.IsActive = Text(t, "Active") != "0";
            task.Mode = Text(t, "Manual") == "1" ? TaskMode.ManuallyScheduled : TaskMode.AutoScheduled;
            task.Type = (Int(t, "Type") ?? 0) switch { 1 => TaskType.FixedDuration, 2 => TaskType.FixedWork, _ => TaskType.FixedUnits };
            task.Priority = Math.Clamp(Int(t, "Priority") ?? 500, 0, 1000);
            int durationFormat = Int(t, "DurationFormat") ?? 7;
            task.IsElapsedDuration = IsElapsedFormat(durationFormat);
            task.IsEstimated = Text(t, "Estimated") == "1";
            task.DurationMinutes = Math.Max(0, Minutes(t, "Duration") ?? 0);
            task.IsMilestone = Text(t, "Milestone") == "1";
            task.IsEffortDriven = Text(t, "EffortDriven") == "1";
            task.ConstraintType = (ConstraintType)Math.Clamp(Int(t, "ConstraintType") ?? 0, 0, 7);
            task.ConstraintDate = Date(t, "ConstraintDate");
            task.Deadline = Date(t, "Deadline");
            task.Calendar = Int(t, "CalendarUID") is { } tc && calendars.TryGetValue(tc, out var taskCal) ? taskCal : null;
            task.IgnoreResourceCalendar = Text(t, "IgnoreResourceCalendar") == "1";
            task.LevelingDelayMinutes = (Long(t, "LevelingDelay") ?? 0) / 10;
            task.PercentComplete = Math.Clamp(Double(t, "PercentComplete") ?? 0, 0, 100);
            task.ActualStart = Date(t, "ActualStart");
            task.ActualFinish = Date(t, "ActualFinish");
            task.FixedCost = Cents(t, "FixedCost") ?? 0;
            task.FixedCostAccrual = ReadAccrual(Int(t, "FixedCostAccrual"));
            task.Notes = Text(t, "Notes") ?? "";
            if (task.Mode == TaskMode.ManuallyScheduled)
            {
                task.ManualStart = Date(t, "ManualStart") ?? Date(t, "Start");
                task.ManualFinish = Date(t, "ManualFinish") ?? Date(t, "Finish");
            }

            foreach (var b in t.Elements().Where(e => e.Name.LocalName == "Baseline"))
            {
                int number = Int(b, "Number") ?? 0;
                if (number is < 0 or >= Model.Baseline.Count || Date(b, "Start") is not { } bs || Date(b, "Finish") is not { } bf)
                    continue;
                task.Baselines[number] = new BaselineValues(bs, bf, Minutes(b, "Duration") ?? 0, Minutes(b, "Work") ?? 0, Cents(b, "Cost") ?? 0, DateTime.Now);
            }
            foreach (var link in t.Elements().Where(e => e.Name.LocalName == "PredecessorLink"))
                links.Add((task, link));
        }
        project.RebuildHierarchy();
        foreach (var summary in summaries)
            summary.DurationMinutes = 0;

        foreach (var (task, link) in links)
        {
            if (Text(link, "CrossProject") == "1" || Int(link, "PredecessorUID") is not { } predUid || project.GetTask(predUid) is not { } pred)
                continue;
            var type = (Int(link, "Type") ?? 1) switch
            {
                0 => LinkType.FinishToFinish,
                2 => LinkType.StartToFinish,
                3 => LinkType.StartToStart,
                _ => LinkType.FinishToStart,
            };
            int lagFormat = Int(link, "LagFormat") ?? 7;
            long lag = Long(link, "LinkLag") ?? 0;
            try
            {
                var created = project.Link(pred, task, type, lagFormat is 19 or 20 ? 0 : lag / 10);
                if (lagFormat is 19 or 20)
                {
                    created.LagFormat = LagFormat.PercentOfPredecessor;
                    created.LagPercent = lag / 10.0;
                }
                else if (IsElapsedFormat(lagFormat))
                {
                    created.LagFormat = LagFormat.ElapsedTime;
                }
            }
            catch (InvalidOperationException)
            {
                // Links Planora does not allow (e.g. between a summary and its own subtask) are skipped.
            }
        }

        // Assignments.
        foreach (var a in Children(root, "Assignments", "Assignment"))
        {
            if (Int(a, "TaskUID") is not { } taskUid || project.GetTask(taskUid) is not { } task)
                continue;
            if (Int(a, "ResourceUID") is not { } resourceUid || project.GetResource(resourceUid) is not { } resource)
                continue; // unassigned placeholder (ResourceUID -65535) or missing resource
            if (task.Assignments.Any(x => x.Resource == resource))
                continue;
            var assignment = Project.RestoreAssignment(task, resource, Double(a, "Units") ?? 1);
            assignment.WorkMinutes = Math.Max(0, Minutes(a, "Work") ?? 0);
            assignment.Contour = (WorkContour)Math.Clamp(Int(a, "WorkContour") ?? 0, 0, 7);
            assignment.CostRateTable = Math.Clamp(Int(a, "CostRateTable") ?? 0, 0, Resource.RateTableCount - 1);
            assignment.DelayMinutes = (Long(a, "Delay") ?? 0) / 10;
            if (resource.Type == ResourceType.Cost)
                assignment.CostAmount = Cents(a, "Cost") ?? 0;
            foreach (var b in a.Elements().Where(e => e.Name.LocalName == "Baseline"))
            {
                int number = Int(b, "Number") ?? 0;
                if (number is < 0 or >= Model.Baseline.Count || Date(b, "Start") is not { } bs || Date(b, "Finish") is not { } bf)
                    continue;
                assignment.Baselines[number] = new BaselineValues(bs, bf, 0, Minutes(b, "Work") ?? 0, Cents(b, "Cost") ?? 0, DateTime.Now);
            }
        }

        return project;
    }

    private static WorkingCalendar ReadCalendar(XElement c, WorkingCalendar? baseCalendar)
    {
        var calendar = new WorkingCalendar(Text(c, "Name") ?? "Calendar", baseCalendar);
        if (baseCalendar is null)
        {
            // A base calendar starts from the Standard week; listed days override it.
            var standard = WorkingCalendar.CreateStandard();
            foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
                calendar.SetWorkWeekDay(day, standard.GetWorkWeekDay(day).ToArray());
        }

        foreach (var weekDay in Children(c, "WeekDays", "WeekDay"))
        {
            int dayType = Int(weekDay, "DayType") ?? -1;
            var times = ReadTimes(weekDay);
            bool working = Text(weekDay, "DayWorking") == "1";
            if (dayType is >= 1 and <= 7)
            {
                calendar.SetWorkWeekDay((DayOfWeek)(dayType - 1), working ? times.ToArray() : Array.Empty<WorkingTime>());
            }
            else if (dayType == 0 && weekDay.Elements().FirstOrDefault(e => e.Name.LocalName == "TimePeriod") is { } period)
            {
                // Older MSPDI files store exceptions as day type 0.
                AddException(calendar, "Exception", period, working ? times : Array.Empty<WorkingTime>());
            }
        }

        foreach (var exception in Children(c, "Exceptions", "Exception"))
        {
            if (exception.Elements().FirstOrDefault(e => e.Name.LocalName == "TimePeriod") is not { } period)
                continue;
            bool working = Text(exception, "DayWorking") == "1";
            AddException(calendar, Text(exception, "Name") ?? "Exception", period, working ? ReadTimes(exception) : Array.Empty<WorkingTime>());
        }
        return calendar;
    }

    private static void AddException(WorkingCalendar calendar, string name, XElement period, IReadOnlyList<WorkingTime> times)
    {
        if (Date(period, "FromDate") is not { } from || Date(period, "ToDate") is not { } to)
            return;
        calendar.AddException(name, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), times.ToArray());
    }

    private static IReadOnlyList<WorkingTime> ReadTimes(XElement parent)
    {
        var times = new List<WorkingTime>();
        foreach (var wt in Children(parent, "WorkingTimes", "WorkingTime"))
        {
            if (Text(wt, "FromTime") is not { } from || Text(wt, "ToTime") is not { } to)
                continue;
            var f = TimeSpan.Parse(from, CultureInfo.InvariantCulture);
            var t = TimeSpan.Parse(to, CultureInfo.InvariantCulture);
            if (t == TimeSpan.Zero)
                t = TimeSpan.FromDays(1);
            if (t > f)
                times.Add(new WorkingTime(f, t));
        }
        try
        {
            return WorkingTime.Normalize(times);
        }
        catch (ArgumentException)
        {
            return times.Take(1).ToList();
        }
    }

    /// <summary>MSPDI format codes 4, 6, 8, 10, 12, 20 (and +32 estimated variants) are elapsed.</summary>
    private static bool IsElapsedFormat(int format) => (format % 32) is 4 or 6 or 8 or 10 or 12 or 20;

    private static CostAccrual ReadAccrual(int? code) => code switch
    {
        1 => CostAccrual.Start,
        2 => CostAccrual.End,
        _ => CostAccrual.Prorated,
    };

    private static IEnumerable<XElement> Children(XElement parent, string container, string item) =>
        parent.Elements().Where(e => e.Name.LocalName == container).Elements().Where(e => e.Name.LocalName == item);

    private static string? Text(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private static int? Int(XElement parent, string name) =>
        int.TryParse(Text(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static long? Long(XElement parent, string name) =>
        long.TryParse(Text(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v
        : double.TryParse(Text(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)Math.Round(d) : null;

    private static double? Double(XElement parent, string name) =>
        double.TryParse(Text(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static decimal? Money(XElement parent, string name) =>
        decimal.TryParse(Text(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static decimal? Cents(XElement parent, string name) => Money(parent, name) is { } cents ? cents / 100m : null;

    private static DateTime? Date(XElement parent, string name) =>
        DateTime.TryParse(Text(parent, name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;

    private static long? Minutes(XElement parent, string name)
    {
        var text = Text(parent, name);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return (long)Math.Round(XmlConvert.ToTimeSpan(text).TotalMinutes);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
