using System.Text.Json;
using System.Text.Json.Serialization;
using Planora.Engine.Calendars;
using Planora.Engine.Fields;
using Planora.Engine.Model;
using Planora.Engine.Views;

namespace Planora.Engine.Persistence;

/// <summary>
/// Reads and writes Planora's native project format (.planora): UTF-8 JSON holding the plan's inputs
/// (tasks, links, calendars, resources, assignments, baselines). Scheduled dates are not stored;
/// the project is rescheduled after loading.
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".planora";
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Save(Project project, string path)
    {
        // Write to a temporary file first so a crash mid-save never corrupts the existing file.
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(project));
        File.Move(temp, path, overwrite: true);
    }

    public static Project Load(string path) => Deserialize(File.ReadAllText(path));

    public static string Serialize(Project project) =>
        JsonSerializer.Serialize(ToDto(project), JsonOptions);

    public static Project Deserialize(string json)
    {
        ProjectDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProjectDto>(json, JsonOptions)
                ?? throw new InvalidDataException("The file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The file is not a valid Planora project: {ex.Message}", ex);
        }
        if (dto.FormatVersion > CurrentFormatVersion)
            throw new InvalidDataException($"This file was made by a newer version of Planora (format {dto.FormatVersion}).");
        return FromDto(dto);
    }

    // ------------------------------------------------------------------ writing

    private static ProjectDto ToDto(Project project)
    {
        var calendars = CollectCalendars(project);
        var calendarIds = calendars.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        int? CalendarId(WorkingCalendar? c) => c is null ? null : calendarIds[c];

        return new ProjectDto
        {
            FormatVersion = CurrentFormatVersion,
            Name = project.Name,
            StartDate = project.StartDate,
            FinishDate = project.FinishDate,
            ScheduleFromFinish = project.ScheduleFromFinish,
            StatusDate = project.StatusDate,
            CalendarId = calendarIds[project.Calendar],
            BaseCalendarIds = project.Calendars.Select(c => calendarIds[c]).ToList(),
            Options = new OptionsDto
            {
                MinutesPerDay = project.Options.MinutesPerDay,
                MinutesPerWeek = project.Options.MinutesPerWeek,
                DaysPerMonth = project.Options.DaysPerMonth,
                CriticalSlackLimitMinutes = project.Options.CriticalSlackLimitMinutes,
                CalculateMultipleCriticalPaths = project.Options.CalculateMultipleCriticalPaths,
                RescheduleUncompletedWorkToStatusDate = project.Options.RescheduleUncompletedWorkToStatusDate,
                DefaultTaskType = project.Options.DefaultTaskType,
                NewTasksEffortDriven = project.Options.NewTasksEffortDriven,
                NewTasksManuallyScheduled = project.Options.NewTasksManuallyScheduled,
            },
            Calendars = calendars.Select(c => new CalendarDto
            {
                Name = c.Name,
                BaseId = CalendarId(c.BaseCalendar),
                Week = Enum.GetValues<DayOfWeek>()
                    .Select(d => c.IsWorkWeekDayInherited(d) ? null : c.GetWorkWeekDay(d).Select(TimeDto.Of).ToList())
                    .ToList(),
                Exceptions = c.Exceptions.Select(e => new ExceptionDto
                {
                    Name = e.Name,
                    From = e.From,
                    To = e.To,
                    Times = e.WorkingTimes.Select(TimeDto.Of).ToList(),
                    RepeatYearly = e.RepeatYearly,
                }).ToList(),
            }).ToList(),
            Resources = project.Resources.Select(r => new ResourceDto
            {
                Id = r.Id,
                Name = r.Name,
                Type = r.Type,
                Initials = r.Initials,
                Group = r.Group,
                Code = r.Code,
                Email = r.Email,
                Notes = r.Notes,
                MaterialLabel = r.MaterialLabel,
                MaxUnits = r.MaxUnits,
                CalendarId = CalendarId(r.Calendar),
                AccrueAt = r.AccrueAt,
                IsBudget = r.IsBudget,
                RateTables = r.CostRateTables.Select(t => t.ToList()).ToList(),
            }).ToList(),
            Tasks = project.Tasks.Select(t => new TaskDto
            {
                Id = t.Id,
                Name = t.Name,
                Notes = t.Notes,
                OutlineLevel = t.OutlineLevel,
                IsActive = t.IsActive,
                Mode = t.Mode,
                DurationMinutes = t.IsSummary ? 0 : t.DurationMinutes,
                IsElapsedDuration = t.IsElapsedDuration,
                IsEstimated = t.IsEstimated,
                IsMilestone = t.IsMilestone,
                ShowOnTimeline = t.ShowOnTimeline,
                ConstraintType = t.ConstraintType,
                ConstraintDate = t.ConstraintDate,
                Deadline = t.Deadline,
                CalendarId = CalendarId(t.Calendar),
                PercentComplete = t.IsSummary ? 0 : t.PercentComplete,
                ManualStart = t.ManualStart,
                ManualFinish = t.ManualFinish,
                ActualStart = t.ActualStart,
                ActualFinish = t.ActualFinish,
                Predecessors = t.Predecessors.Select(l => new LinkDto
                {
                    PredecessorId = l.PredecessorId,
                    Type = l.Type,
                    LagFormat = l.LagFormat,
                    LagMinutes = l.LagMinutes,
                    LagPercent = l.LagPercent,
                }).ToList(),
                Splits = t.Splits.ToList(),
                Recurrence = t.Recurrence,
                Type = t.Type,
                IsEffortDriven = t.IsEffortDriven,
                IgnoreResourceCalendar = t.IgnoreResourceCalendar,
                FixedCost = t.FixedCost,
                FixedCostAccrual = t.FixedCostAccrual,
                Priority = t.Priority,
                LevelingDelayMinutes = t.LevelingDelayMinutes,
                Baselines = Sparse(t.Baselines),
                InterimPlans = SparseValues(t.InterimPlans),
                Custom = t.CustomValues.Count == 0 ? null : t.CustomValues.ToDictionary(kv => kv.Key, kv => FieldValue.ToStorage(kv.Value)!),
                Assignments = t.Assignments.Select(a => new AssignmentDto
                {
                    ResourceId = a.Resource.Id,
                    Units = a.Units,
                    WorkMinutes = a.WorkMinutes,
                    CostAmount = a.CostAmount,
                    Contour = a.Contour,
                    CostRateTable = a.CostRateTable,
                    DelayMinutes = a.DelayMinutes,
                    Baselines = Sparse(a.Baselines),
                }).ToList(),
            }).ToList(),
            CustomFields = project.CustomFields.Select(cf => new CustomFieldDto { Key = cf.Key, Alias = cf.Alias, ValueList = cf.ValueList.ToList(), Formula = cf.Formula }).ToList(),
            Tables = project.Tables.Select(t => new TableDto { Name = t.Name, Columns = t.Columns.ToList() }).ToList(),
            Filters = project.Filters.Select(fl => new FilterDto { Name = fl.Name, Conditions = fl.Conditions.ToList(), ShowRelatedSummaryRows = fl.ShowRelatedSummaryRows }).ToList(),
            Groups = project.Groups.Select(g => new GroupDto { Name = g.Name, Levels = g.Levels.ToList(), ShowSummaryTasks = g.ShowSummaryTasks }).ToList(),
            SavedViews = project.SavedViews.Select(v => new SavedViewDto
            {
                Name = v.Name, Screen = v.Screen, Table = v.Table, Filter = v.Filter, Group = v.Group,
                SortKeys = v.Sort.Keys.ToList(), KeepOutlineStructure = v.Sort.KeepOutlineStructure,
            }).ToList(),
            BudgetCosts = project.BudgetCosts.ToDictionary(kv => kv.Key.Id, kv => kv.Value),
            BudgetWork = project.BudgetWork.ToDictionary(kv => kv.Key.Id, kv => kv.Value),
            Baselines = Sparse(project.Baselines),
        };
    }

    /// <summary>Every calendar in use, with base calendars listed before calendars derived from them.</summary>
    private static List<WorkingCalendar> CollectCalendars(Project project)
    {
        var result = new List<WorkingCalendar>();
        void Add(WorkingCalendar? calendar)
        {
            if (calendar is null || result.Contains(calendar))
                return;
            Add(calendar.BaseCalendar);
            result.Add(calendar);
        }

        Add(project.Calendar);
        project.Calendars.ForEach(Add);
        foreach (var r in project.Resources)
            Add(r.Calendar);
        foreach (var t in project.Tasks)
            Add(t.Calendar);
        return result;
    }

    private static Dictionary<int, T>? Sparse<T>(T?[] values) where T : class
    {
        var result = new Dictionary<int, T>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] is { } v)
                result[i] = v;
        }
        return result.Count == 0 ? null : result;
    }

    private static Dictionary<int, T>? SparseValues<T>(T?[] values) where T : struct
    {
        var result = new Dictionary<int, T>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] is { } v)
                result[i] = v;
        }
        return result.Count == 0 ? null : result;
    }

    // ------------------------------------------------------------------ reading

    private static Project FromDto(ProjectDto dto)
    {
        var calendars = new List<WorkingCalendar>();
        foreach (var c in dto.Calendars)
        {
            WorkingCalendar? baseCalendar = c.BaseId is { } baseId ? CalendarAt(calendars, baseId) : null;
            var calendar = new WorkingCalendar(c.Name, baseCalendar);
            for (int d = 0; d < 7; d++)
            {
                var times = d < c.Week.Count ? c.Week[d] : null;
                if (times is not null)
                    calendar.SetWorkWeekDay((DayOfWeek)d, times.Select(t => t.ToWorkingTime()).ToArray());
                else if (baseCalendar is null)
                    calendar.SetWorkWeekDay((DayOfWeek)d);
            }
            foreach (var e in c.Exceptions)
                calendar.AddException(e.Name, e.From, e.To, e.Times.Select(t => t.ToWorkingTime()).ToArray()).RepeatYearly = e.RepeatYearly;
            calendars.Add(calendar);
        }

        var project = new Project(dto.Name, dto.StartDate)
        {
            FinishDate = dto.FinishDate,
            ScheduleFromFinish = dto.ScheduleFromFinish,
            StatusDate = dto.StatusDate,
        };
        project.Calendars.Clear();
        if (calendars.Count > 0)
        {
            project.Calendar = CalendarAt(calendars, dto.CalendarId);
            project.Calendars.AddRange(dto.BaseCalendarIds.Select(id => CalendarAt(calendars, id)));
            if (!project.Calendars.Contains(project.Calendar))
                project.Calendars.Insert(0, project.Calendar);
        }
        else
        {
            project.Calendars.Add(project.Calendar);
        }

        if (dto.Options is { } o)
        {
            project.Options.MinutesPerDay = o.MinutesPerDay;
            project.Options.MinutesPerWeek = o.MinutesPerWeek;
            project.Options.DaysPerMonth = o.DaysPerMonth;
            project.Options.CriticalSlackLimitMinutes = o.CriticalSlackLimitMinutes;
            project.Options.CalculateMultipleCriticalPaths = o.CalculateMultipleCriticalPaths;
            project.Options.RescheduleUncompletedWorkToStatusDate = o.RescheduleUncompletedWorkToStatusDate;
            project.Options.DefaultTaskType = o.DefaultTaskType;
            project.Options.NewTasksEffortDriven = o.NewTasksEffortDriven;
            project.Options.NewTasksManuallyScheduled = o.NewTasksManuallyScheduled;
        }

        foreach (var r in dto.Resources)
        {
            var resource = project.RestoreResource(r.Id, r.Name, r.Type, r.CalendarId is { } cid ? CalendarAt(calendars, cid) : null);
            resource.Initials = r.Initials;
            resource.Group = r.Group;
            resource.Code = r.Code;
            resource.Email = r.Email;
            resource.Notes = r.Notes;
            resource.MaterialLabel = r.MaterialLabel;
            resource.MaxUnits = r.MaxUnits;
            resource.AccrueAt = r.AccrueAt;
            resource.IsBudget = r.IsBudget;
            for (int i = 0; i < Resource.RateTableCount && i < r.RateTables.Count; i++)
            {
                if (r.RateTables[i].Count == 0)
                    continue;
                resource.CostRateTables[i].Clear();
                resource.CostRateTables[i].AddRange(r.RateTables[i].OrderBy(x => x.EffectiveFrom));
            }
        }

        foreach (var t in dto.Tasks)
        {
            var task = project.RestoreTask(t.Id, t.Name, t.OutlineLevel);
            task.Notes = t.Notes;
            task.IsActive = t.IsActive;
            task.Mode = t.Mode;
            task.DurationMinutes = t.DurationMinutes;
            task.IsElapsedDuration = t.IsElapsedDuration;
            task.IsEstimated = t.IsEstimated;
            task.IsMilestone = t.IsMilestone;
            task.ShowOnTimeline = t.ShowOnTimeline;
            task.ConstraintType = t.ConstraintType;
            task.ConstraintDate = t.ConstraintDate;
            task.Deadline = t.Deadline;
            task.Calendar = t.CalendarId is { } cid ? CalendarAt(calendars, cid) : null;
            task.PercentComplete = t.PercentComplete;
            task.ManualStart = t.ManualStart;
            task.ManualFinish = t.ManualFinish;
            task.ActualStart = t.ActualStart;
            task.ActualFinish = t.ActualFinish;
            foreach (var l in t.Predecessors)
            {
                task.Predecessors.Add(new TaskLink(l.PredecessorId, l.Type, l.LagMinutes)
                {
                    LagFormat = l.LagFormat,
                    LagPercent = l.LagPercent,
                });
            }
            foreach (var s in t.Splits)
                task.Split(s.AfterWorkMinutes, s.GapMinutes);
            task.Recurrence = t.Recurrence;
            task.Type = t.Type;
            task.IsEffortDriven = t.IsEffortDriven;
            task.IgnoreResourceCalendar = t.IgnoreResourceCalendar;
            task.FixedCost = t.FixedCost;
            task.FixedCostAccrual = t.FixedCostAccrual;
            task.Priority = t.Priority;
            task.LevelingDelayMinutes = t.LevelingDelayMinutes;
            Fill(task.Baselines, t.Baselines);
            FillValues(task.InterimPlans, t.InterimPlans);
            foreach (var (key, stored) in t.Custom ?? new())
            {
                if (CustomField.TypeOfKey(key) is not { } type)
                    throw new InvalidDataException($"Task {t.Id} has a value for unknown field '{key}'.");
                if (FieldValue.FromStorage(stored, type) is { } value)
                    task.CustomValues[key] = value;
            }

            foreach (var a in t.Assignments)
            {
                var resource = project.GetResource(a.ResourceId)
                    ?? throw new InvalidDataException($"Task {t.Id} is assigned to missing resource {a.ResourceId}.");
                var assignment = Project.RestoreAssignment(task, resource, a.Units);
                assignment.WorkMinutes = a.WorkMinutes;
                assignment.CostAmount = a.CostAmount;
                assignment.Contour = a.Contour;
                assignment.CostRateTable = a.CostRateTable;
                assignment.DelayMinutes = a.DelayMinutes;
                Fill(assignment.Baselines, a.Baselines);
            }
        }
        project.RebuildHierarchy();

        foreach (var task in project.Tasks)
        {
            if (task.Predecessors.FirstOrDefault(l => project.GetTask(l.PredecessorId) is null) is { } broken)
                throw new InvalidDataException($"Task {task.Id} depends on missing task {broken.PredecessorId}.");
        }

        foreach (var (id, amount) in dto.BudgetCosts ?? new())
            project.BudgetCosts[RequireResource(project, id)] = amount;
        foreach (var (id, amount) in dto.BudgetWork ?? new())
            project.BudgetWork[RequireResource(project, id)] = amount;
        Fill(project.Baselines, dto.Baselines);

        foreach (var cf in dto.CustomFields ?? new())
            project.CustomFields.Add(new CustomField(cf.Key, cf.Alias) { ValueList = cf.ValueList, Formula = cf.Formula });
        foreach (var t in dto.Tables ?? new())
            project.Tables.Add(new TableDefinition(t.Name, t.Columns.ToArray()));
        foreach (var fl in dto.Filters ?? new())
            project.Filters.Add(new FilterDefinition(fl.Name, fl.Conditions.ToArray()) { ShowRelatedSummaryRows = fl.ShowRelatedSummaryRows });
        foreach (var g in dto.Groups ?? new())
            project.Groups.Add(new GroupDefinition(g.Name, g.Levels.ToArray()) { ShowSummaryTasks = g.ShowSummaryTasks });
        foreach (var v in dto.SavedViews ?? new())
        {
            project.SavedViews.Add(new SavedView
            {
                Name = v.Name, Screen = v.Screen, Table = v.Table, Filter = v.Filter, Group = v.Group,
                Sort = new SortDefinition { Keys = v.SortKeys, KeepOutlineStructure = v.KeepOutlineStructure },
            });
        }
        return project;
    }

    private static WorkingCalendar CalendarAt(List<WorkingCalendar> calendars, int id) =>
        id >= 0 && id < calendars.Count ? calendars[id] : throw new InvalidDataException($"Calendar {id} does not exist.");

    private static Resource RequireResource(Project project, int id) =>
        project.GetResource(id) ?? throw new InvalidDataException($"Resource {id} does not exist.");

    private static void Fill<T>(T?[] target, Dictionary<int, T>? source) where T : class
    {
        foreach (var (i, v) in source ?? new())
        {
            if (i >= 0 && i < target.Length)
                target[i] = v;
        }
    }

    private static void FillValues<T>(T?[] target, Dictionary<int, T>? source) where T : struct
    {
        foreach (var (i, v) in source ?? new())
        {
            if (i >= 0 && i < target.Length)
                target[i] = v;
        }
    }

    // ------------------------------------------------------------------ file shape

    private sealed class ProjectDto
    {
        public int FormatVersion { get; set; }
        public string Name { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime FinishDate { get; set; }
        public bool ScheduleFromFinish { get; set; }
        public DateTime? StatusDate { get; set; }
        public int CalendarId { get; set; }
        public List<int> BaseCalendarIds { get; set; } = new();
        public OptionsDto? Options { get; set; }
        public List<CalendarDto> Calendars { get; set; } = new();
        public List<ResourceDto> Resources { get; set; } = new();
        public List<TaskDto> Tasks { get; set; } = new();
        public Dictionary<int, decimal>? BudgetCosts { get; set; }
        public Dictionary<int, double>? BudgetWork { get; set; }
        public Dictionary<int, BaselineValues>? Baselines { get; set; }
        public List<CustomFieldDto>? CustomFields { get; set; }
        public List<TableDto>? Tables { get; set; }
        public List<FilterDto>? Filters { get; set; }
        public List<GroupDto>? Groups { get; set; }
        public List<SavedViewDto>? SavedViews { get; set; }
    }

    private sealed class CustomFieldDto
    {
        public string Key { get; set; } = "";
        public string Alias { get; set; } = "";
        public List<string> ValueList { get; set; } = new();
        public string? Formula { get; set; }
    }

    private sealed class TableDto
    {
        public string Name { get; set; } = "";
        public List<TableColumn> Columns { get; set; } = new();
    }

    private sealed class FilterDto
    {
        public string Name { get; set; } = "";
        public List<FilterCondition> Conditions { get; set; } = new();
        public bool ShowRelatedSummaryRows { get; set; }
    }

    private sealed class GroupDto
    {
        public string Name { get; set; } = "";
        public List<GroupLevel> Levels { get; set; } = new();
        public bool ShowSummaryTasks { get; set; }
    }

    private sealed class SavedViewDto
    {
        public string Name { get; set; } = "";
        public string Screen { get; set; } = "Gantt";
        public string Table { get; set; } = "Entry";
        public string Filter { get; set; } = "All Tasks";
        public string Group { get; set; } = "No Group";
        public List<SortKey> SortKeys { get; set; } = new();
        public bool KeepOutlineStructure { get; set; } = true;
    }

    private sealed class OptionsDto
    {
        public int MinutesPerDay { get; set; } = 480;
        public int MinutesPerWeek { get; set; } = 2400;
        public int DaysPerMonth { get; set; } = 20;
        public long CriticalSlackLimitMinutes { get; set; }
        public bool CalculateMultipleCriticalPaths { get; set; }
        public bool RescheduleUncompletedWorkToStatusDate { get; set; }
        public TaskType DefaultTaskType { get; set; }
        public bool NewTasksEffortDriven { get; set; }
        public bool NewTasksManuallyScheduled { get; set; }
    }

    private sealed class CalendarDto
    {
        public string Name { get; set; } = "";
        public int? BaseId { get; set; }

        /// <summary>Sunday..Saturday; null entries inherit from the base calendar.</summary>
        public List<List<TimeDto>?> Week { get; set; } = new();
        public List<ExceptionDto> Exceptions { get; set; } = new();
    }

    private sealed class ExceptionDto
    {
        public string Name { get; set; } = "";
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public List<TimeDto> Times { get; set; } = new();
        public bool RepeatYearly { get; set; }
    }

    private sealed class TimeDto
    {
        public TimeSpan From { get; set; }
        public TimeSpan To { get; set; }

        public static TimeDto Of(WorkingTime t) => new() { From = t.From, To = t.To };

        public WorkingTime ToWorkingTime() => new(From, To);
    }

    private sealed class ResourceDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ResourceType Type { get; set; }
        public string Initials { get; set; } = "";
        public string Group { get; set; } = "";
        public string Code { get; set; } = "";
        public string Email { get; set; } = "";
        public string Notes { get; set; } = "";
        public string MaterialLabel { get; set; } = "";
        public double MaxUnits { get; set; } = 1.0;
        public int? CalendarId { get; set; }
        public CostAccrual AccrueAt { get; set; } = CostAccrual.Prorated;
        public bool IsBudget { get; set; }
        public List<List<CostRate>> RateTables { get; set; } = new();
    }

    private sealed class TaskDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Notes { get; set; } = "";
        public int OutlineLevel { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public TaskMode Mode { get; set; }
        public long DurationMinutes { get; set; }
        public bool IsElapsedDuration { get; set; }
        public bool IsEstimated { get; set; }
        public bool IsMilestone { get; set; }
        public bool ShowOnTimeline { get; set; }
        public ConstraintType ConstraintType { get; set; }
        public DateTime? ConstraintDate { get; set; }
        public DateTime? Deadline { get; set; }
        public int? CalendarId { get; set; }
        public double PercentComplete { get; set; }
        public DateTime? ManualStart { get; set; }
        public DateTime? ManualFinish { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }
        public List<LinkDto> Predecessors { get; set; } = new();
        public List<TaskSplit> Splits { get; set; } = new();
        public RecurrencePattern? Recurrence { get; set; }
        public TaskType Type { get; set; }
        public bool IsEffortDriven { get; set; }
        public bool IgnoreResourceCalendar { get; set; }
        public decimal FixedCost { get; set; }
        public CostAccrual FixedCostAccrual { get; set; } = CostAccrual.Prorated;
        public int Priority { get; set; } = 500;
        public long LevelingDelayMinutes { get; set; }
        public Dictionary<int, BaselineValues>? Baselines { get; set; }
        public Dictionary<int, InterimPlan>? InterimPlans { get; set; }
        public Dictionary<string, string>? Custom { get; set; }
        public List<AssignmentDto> Assignments { get; set; } = new();
    }

    private sealed class LinkDto
    {
        public int PredecessorId { get; set; }
        public LinkType Type { get; set; }
        public LagFormat LagFormat { get; set; }
        public long LagMinutes { get; set; }
        public double LagPercent { get; set; }
    }

    private sealed class AssignmentDto
    {
        public int ResourceId { get; set; }
        public double Units { get; set; }
        public long WorkMinutes { get; set; }
        public decimal CostAmount { get; set; }
        public WorkContour Contour { get; set; }
        public int CostRateTable { get; set; }
        public long DelayMinutes { get; set; }
        public Dictionary<int, BaselineValues>? Baselines { get; set; }
    }
}
