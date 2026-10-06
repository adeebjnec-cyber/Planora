using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Interop;

/// <summary>
/// Task splits and hand-edited (contoured) work in Microsoft Project XML. Microsoft Project keeps both as
/// the assignments' time-phased work: one period per working segment or piece, and zero-work periods for
/// gaps (Type 1 remaining work, Type 2 actual work). Unassigned split tasks use a placeholder assignment
/// to resource -65535.
/// (Checked by reading the result back with MPXJ, whose split detection follows Microsoft Project's files.)
/// </summary>
internal static class MspdiSplits
{
    private const int UnassignedResource = -65535;

    /// <summary>Adds time-phased data for every split task; returns placeholder assignments for unassigned split tasks.</summary>
    public static List<XElement> Write(XNamespace ns, Project project, XElement assignments)
    {
        var placeholders = new List<XElement>();
        int nextUid = assignments.Elements().Count() + 1;
        var byTask = assignments.Elements().GroupBy(a => (int?)a.Element(ns + "TaskUID")).ToDictionary(g => g.Key ?? -1, g => g.ToList());
        foreach (var task in project.Tasks.Where(t => !t.IsSummary && (t.Segments.Count > 1 || t.Assignments.Any(a => a.IsContoured))))
        {
            var cal = task.Calendar ?? project.Calendar;
            var work = task.Assignments.Where(a => a.IsWork && a.WorkMinutes > 0).ToList();
            if (work.Count > 0 && byTask.TryGetValue(task.Id, out var elements))
            {
                foreach (var element in elements)
                {
                    int resourceUid = (int?)element.Element(ns + "ResourceUID") ?? 0;
                    if (work.FirstOrDefault(a => a.Resource.Id == resourceUid) is not { } assignment)
                        continue;
                    int uid = (int?)element.Element(ns + "UID") ?? 0;
                    if (assignment.IsContoured)
                        element.Add(PiecePeriods(ns, uid, assignment, project));
                    else if (task.Segments.Count > 1)
                        element.Add(Periods(ns, uid, task, cal, (from, to) => Timephased.WorkBetween(assignment, from, to)));
                }
            }
            else if (work.Count == 0 && task.Segments.Count > 1)
            {
                int uid = nextUid++;
                var placeholder = new XElement(ns + "Assignment",
                    new XElement(ns + "UID", uid),
                    new XElement(ns + "TaskUID", task.Id),
                    new XElement(ns + "ResourceUID", UnassignedResource),
                    new XElement(ns + "PercentWorkComplete", (int)Math.Round(task.PercentComplete)),
                    new XElement(ns + "Units", 1),
                    new XElement(ns + "Work", Duration(task.DurationMinutes)),
                    new XElement(ns + "RemainingWork", Duration(task.RemainingDurationMinutes)),
                    new XElement(ns + "Start", Text(task.Start)),
                    new XElement(ns + "Finish", Text(task.Finish)));
                placeholder.Add(Periods(ns, uid, task, cal, (from, to) => cal.WorkingMinutesBetween(from, to)));
                placeholders.Add(placeholder);
            }
        }
        return placeholders;
    }

    /// <summary>Work periods for each segment (split at the complete-through point) and zero-work gaps.</summary>
    private static IEnumerable<XElement> Periods(XNamespace ns, int uid, ProjectTask task, Calendars.WorkingCalendar cal, Func<DateTime, DateTime, double> workBetween)
    {
        var doneUntil = task.PercentComplete >= 100 ? task.Finish : task.PercentComplete > 0 ? task.CompleteThrough : DateTime.MinValue;
        TaskSegment? previous = null;
        foreach (var segment in task.Segments)
        {
            if (previous is { } prev)
                yield return Period(ns, uid, prev.Finish <= doneUntil ? 2 : 1, prev.Finish, segment.Start, 0);
            var cut = doneUntil < segment.Start ? segment.Start : doneUntil > segment.Finish ? segment.Finish : doneUntil;
            if (cut > segment.Start)
                yield return Period(ns, uid, 2, segment.Start, cut, workBetween(segment.Start, cut));
            if (cut < segment.Finish)
                yield return Period(ns, uid, 1, cut, segment.Finish, workBetween(cut, segment.Finish));
            previous = segment;
        }
    }

    /// <summary>A contoured assignment's hand-edited pieces, as work periods (Type 2 up to the complete-through point).</summary>
    private static IEnumerable<XElement> PiecePeriods(XNamespace ns, int uid, Assignment assignment, Project project)
    {
        var cal = assignment.EffectiveCalendar ?? assignment.Resource.Calendar ?? project.Calendar;
        var task = assignment.Task;
        var doneUntil = task.PercentComplete >= 100 ? task.Finish : task.PercentComplete > 0 ? task.CompleteThrough : DateTime.MinValue;
        var start = assignment.Start;
        foreach (var piece in assignment.Pieces)
        {
            var from = cal.AddWorkingMinutes(start, piece.Offset);
            var to = cal.AddWorkingMinutes(start, piece.End);
            yield return Period(ns, uid, to <= doneUntil ? 2 : 1, from, to, piece.WorkMinutes);
        }
    }

    private static XElement Period(XNamespace ns, int uid, int type, DateTime start, DateTime finish, double minutes) =>
        new(ns + "TimephasedData",
            new XElement(ns + "Type", type),
            new XElement(ns + "UID", uid),
            new XElement(ns + "Start", Text(start)),
            new XElement(ns + "Finish", Text(finish)),
            new XElement(ns + "Unit", 2),
            new XElement(ns + "Value", Duration((long)Math.Round(minutes))));

    private static string Text(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static string Duration(long minutes) => XmlConvert.ToString(TimeSpan.FromMinutes(Math.Max(0, minutes)));

    /// <summary>
    /// Rebuilds task splits from the time-phased work of each task's assignments: a zero-work period that
    /// covers working time between two periods with work is a split. Tasks whose periods do not add up to
    /// their duration are left alone.
    /// </summary>
    public static void Read(XElement root, Project project)
    {
        var periodsByTask = new Dictionary<int, List<(DateTime Start, DateTime Finish, bool HasWork)>>();
        var contouredTasks = new HashSet<int>();
        foreach (var assignment in root.Elements().Where(e => e.Name.LocalName == "Assignments").SelectMany(e => e.Elements()))
        {
            if (!int.TryParse(Child(assignment, "TaskUID"), out var taskUid))
                continue;
            if (Child(assignment, "WorkContour") == "8")
            {
                contouredTasks.Add(taskUid);
                ReadPieces(assignment, project, taskUid);
                continue;
            }
            foreach (var data in assignment.Elements().Where(e => e.Name.LocalName == "TimephasedData"))
            {
                if (Child(data, "Type") is not ("1" or "2") || !DateTime.TryParse(Child(data, "Start"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                    || !DateTime.TryParse(Child(data, "Finish"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var finish) || finish <= start)
                    continue;
                double minutes;
                try
                {
                    minutes = XmlConvert.ToTimeSpan(Child(data, "Value") ?? "PT0M").TotalMinutes;
                }
                catch (FormatException)
                {
                    continue;
                }
                if (!periodsByTask.TryGetValue(taskUid, out var list))
                    periodsByTask[taskUid] = list = new();
                list.Add((start, finish, minutes > 0.5));
            }
        }

        foreach (var (taskUid, periods) in periodsByTask)
        {
            if (project.GetTask(taskUid) is not { IsSummary: false } task || task.Splits.Count > 0 || contouredTasks.Contains(taskUid))
                continue;
            var cal = task.Calendar ?? project.Calendar;
            // Working segments: runs of periods with work, separated by zero-work periods covering working time.
            var segments = new List<(DateTime Start, DateTime Finish)>();
            foreach (var p in periods.OrderBy(p => p.Start))
            {
                if (p.HasWork)
                {
                    if (segments.Count > 0 && cal.WorkingMinutesBetween(segments[^1].Finish, p.Start) <= 0)
                        segments[^1] = (segments[^1].Start, p.Finish > segments[^1].Finish ? p.Finish : segments[^1].Finish);
                    else
                        segments.Add((p.Start, p.Finish));
                }
            }
            if (segments.Count < 2)
                continue;
            long total = segments.Sum(s => cal.WorkingMinutesBetween(s.Start, s.Finish));
            if (Math.Abs(total - task.DurationMinutes) > Math.Max(60, task.DurationMinutes / 20))
                continue;
            long done = 0;
            for (int i = 0; i + 1 < segments.Count; i++)
            {
                done += cal.WorkingMinutesBetween(segments[i].Start, segments[i].Finish);
                long gap = cal.WorkingMinutesBetween(segments[i].Finish, segments[i + 1].Start);
                if (gap > 0 && done < task.DurationMinutes)
                    task.Split(done, gap);
            }
        }
    }

    /// <summary>A contoured assignment's time-phased work becomes its hand-edited pieces.</summary>
    private static void ReadPieces(XElement element, Project project, int taskUid)
    {
        if (project.GetTask(taskUid) is not { } task || !int.TryParse(Child(element, "ResourceUID"), out var resourceUid)
            || task.Assignments.FirstOrDefault(a => a.Resource.Id == resourceUid) is not { IsWork: true } assignment
            || !DateTime.TryParse(Child(element, "Start"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return;
        var cal = assignment.Resource.Calendar ?? task.Calendar ?? project.Calendar;
        var pieces = new List<WorkPiece>();
        foreach (var data in element.Elements().Where(e => e.Name.LocalName == "TimephasedData").OrderBy(e => Child(e, "Start")))
        {
            if (Child(data, "Type") is not ("1" or "2") || !DateTime.TryParse(Child(data, "Start"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                || !DateTime.TryParse(Child(data, "Finish"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) || to <= from)
                continue;
            long work;
            try
            {
                work = (long)Math.Round(XmlConvert.ToTimeSpan(Child(data, "Value") ?? "PT0M").TotalMinutes);
            }
            catch (FormatException)
            {
                continue;
            }
            long offset = cal.WorkingMinutesBetween(start, from);
            long length = cal.WorkingMinutesBetween(from, to);
            if (offset >= 0 && length > 0)
                pieces.Add(new WorkPiece(offset, length, work));
        }
        if (pieces.Count > 0)
            assignment.SetPieces(pieces);
    }

    private static string? Child(XElement parent, string name) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
}
