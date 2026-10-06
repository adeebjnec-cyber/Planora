using System.Xml.Linq;

namespace Planora.Engine.Interop;

/// <summary>
/// The element order of Microsoft Project XML (MSPDI), as defined by Microsoft's schema (taken from the
/// schema bindings in MPXJ). Microsoft Project validates files against the schema, so children must come in
/// this order and unknown elements are not allowed. <see cref="Normalize"/> puts a document in order and
/// removes elements the schema does not define.
/// </summary>
public static class MspdiSchema
{
    private static readonly Dictionary<string, string> Orders = new()
    {
        ["Project"] =
            "SaveVersion,UID,Name,GUID,Title,Subject,Category,Company,Manager,Author,CreationDate,Revision,LastSaved," +
            "ScheduleFromStart,StartDate,FinishDate,FYStartDate,CriticalSlackLimit,CurrencyDigits,CurrencySymbol," +
            "CurrencyCode,CurrencySymbolPosition,CalendarUID,DefaultStartTime,DefaultFinishTime,MinutesPerDay," +
            "MinutesPerWeek,DaysPerMonth,DefaultTaskType,DefaultFixedCostAccrual,DefaultStandardRate,DefaultOvertimeRate," +
            "DurationFormat,WorkFormat,EditableActualCosts,HonorConstraints,EarnedValueMethod,InsertedProjectsLikeSummary," +
            "MultipleCriticalPaths,NewTasksEffortDriven,NewTasksEstimated,SplitsInProgressTasks,SpreadActualCost," +
            "SpreadPercentComplete,TaskUpdatesResource,FiscalYearStart,WeekStartDay,MoveCompletedEndsBack," +
            "MoveRemainingStartsBack,MoveRemainingStartsForward,MoveCompletedEndsForward,BaselineForEarnedValue," +
            "AutoAddNewResourcesAndTasks,StatusDate,CurrentDate,MicrosoftProjectServerURL,Autolink,NewTaskStartDate," +
            "NewTasksAreManual,DefaultTaskEVMethod,ProjectExternallyEdited,ExtendedCreationDate,ActualsInSync," +
            "RemoveFileProperties,AdminProject,BaselineCalendar,UpdateManuallyScheduledTasksWhenEditingLinks," +
            "KeepTaskOnNearestWorkingTimeWhenMadeAutoScheduled,OutlineCodes,WBSMasks,ExtendedAttributes,Calendars,Tasks," +
            "Resources,Assignments",
        ["Project/OutlineCodes"] =
            "OutlineCode",
        ["Project/WBSMasks"] =
            "VerifyUniqueCodes,GenerateCodes,Prefix,WBSMask",
        ["Project/ExtendedAttributes"] =
            "ExtendedAttribute",
        ["Project/Calendars"] =
            "Calendar",
        ["Project/Tasks"] =
            "Task",
        ["Project/Resources"] =
            "Resource",
        ["Project/Assignments"] =
            "Assignment",
        ["Project/OutlineCodes/OutlineCode"] =
            "Guid,FieldID,FieldName,Alias,PhoneticAlias,Values,Enterprise,EnterpriseOutlineCodeAlias," +
            "ResourceSubstitutionEnabled,LeafOnly,AllLevelsRequired,OnlyTableValuesAllowed,ShowIndent,Masks",
        ["Project/WBSMasks/WBSMask"] =
            "Level,Type,Length,Separator",
        ["Project/ExtendedAttributes/ExtendedAttribute"] =
            "FieldID,FieldName,CFType,Guid,ElemType,MaxMultiValues,UserDef,Alias,SecondaryPID,AutoRollDown,DefaultGuid," +
            "Ltuid,SecondaryGuid,PhoneticAlias,RollupType,CalculationType,Formula,RestrictValues,ValuelistSortOrder," +
            "AppendNewValues,Default,ValueList",
        ["Project/Calendars/Calendar"] =
            "UID,GUID,Name,IsBaseCalendar,IsBaselineCalendar,BaseCalendarUID,WeekDays,Exceptions,WorkWeeks",
        ["Project/Tasks/Task"] =
            "UID,GUID,ID,Name,Active,Manual,Type,IsNull,CreateDate,Contact,WBS,WBSLevel,OutlineNumber,OutlineLevel,Priority," +
            "Start,Finish,Duration,DurationFormat,Work,Stop,Resume,ResumeValid,EffortDriven,Recurring,OverAllocated," +
            "Estimated,Milestone,Summary,DisplayAsSummary,Critical,IsSubproject,IsSubprojectReadOnly,SubprojectName," +
            "ExternalTask,ExternalTaskProject,EarlyStart,EarlyFinish,LateStart,LateFinish,StartVariance,FinishVariance," +
            "WorkVariance,FreeSlack,TotalSlack,StartSlack,FinishSlack,FixedCost,FixedCostAccrual,PercentComplete," +
            "PercentWorkComplete,Cost,OvertimeCost,OvertimeWork,ActualStart,ActualFinish,ActualDuration,ActualCost," +
            "ActualOvertimeCost,ActualWork,ActualOvertimeWork,RegularWork,RemainingDuration,RemainingCost,RemainingWork," +
            "RemainingOvertimeCost,RemainingOvertimeWork,ACWP,CV,ConstraintType,CalendarUID,ConstraintDate,Deadline," +
            "LevelAssignments,LevelingCanSplit,LevelingDelay,LevelingDelayFormat,PreLeveledStart,PreLeveledFinish,Hyperlink," +
            "HyperlinkAddress,HyperlinkSubAddress,IgnoreResourceCalendar,Notes,HideBar,Rollup,BCWS,BCWP," +
            "PhysicalPercentComplete,EarnedValueMethod,PredecessorLink,ActualWorkProtected,ActualOvertimeWorkProtected," +
            "ExtendedAttribute,Baseline,OutlineCode,IsPublished,StatusManager,CommitmentStart,CommitmentFinish," +
            "CommitmentType,StartText,FinishText,DurationText,ManualStart,ManualFinish,ManualDuration,TimephasedData," +
            "Project",
        ["Project/Resources/Resource"] =
            "UID,GUID,ID,Name,Type,IsNull,Initials,Phonetics,NTAccount,MaterialLabel,Code,Group,WorkGroup,EmailAddress," +
            "Hyperlink,HyperlinkAddress,HyperlinkSubAddress,MaxUnits,PeakUnits,OverAllocated,AvailableFrom,AvailableTo," +
            "Start,Finish,CanLevel,AccrueAt,Work,RegularWork,OvertimeWork,ActualWork,RemainingWork,ActualOvertimeWork," +
            "RemainingOvertimeWork,PercentWorkComplete,StandardRate,StandardRateFormat,Cost,OvertimeRate,OvertimeRateFormat," +
            "OvertimeCost,CostPerUse,ActualCost,ActualOvertimeCost,RemainingCost,RemainingOvertimeCost,WorkVariance," +
            "CostVariance,SV,CV,ACWP,CalendarUID,Notes,BCWS,BCWP,IsGeneric,IsInactive,IsEnterprise,BookingType," +
            "ActualWorkProtected,ActualOvertimeWorkProtected,ActiveDirectoryGUID,CreationDate,ExtendedAttribute,Baseline," +
            "OutlineCode,CostCenter,IsCostResource,AssnOwner,AssnOwnerGuid,IsBudget,AvailabilityPeriods,Rates," +
            "TimephasedData",
        ["Project/Assignments/Assignment"] =
            "UID,GUID,TaskUID,ResourceUID,PercentWorkComplete,ActualCost,ActualFinish,ActualOvertimeCost,ActualOvertimeWork," +
            "ActualStart,ActualWork,ACWP,Confirmed,Cost,CostRateTable,CostVariance,CV,Delay,Finish,FinishVariance,Hyperlink," +
            "HyperlinkAddress,HyperlinkSubAddress,WorkVariance,HasFixedRateUnits,FixedMaterial,LevelingDelay," +
            "LevelingDelayFormat,LinkedFields,Milestone,Notes,Overallocated,OvertimeCost,OvertimeWork,PeakUnits,RateScale," +
            "RegularWork,RemainingCost,RemainingOvertimeCost,RemainingOvertimeWork,RemainingWork,ResponsePending,Start,Stop," +
            "Resume,StartVariance,Summary,SV,Units,UpdateNeeded,VAC,Work,WorkContour,BCWS,BCWP,BookingType," +
            "ActualWorkProtected,ActualOvertimeWorkProtected,CreationDate,AssnOwner,AssnOwnerGuid,BudgetCost,BudgetWork," +
            "ExtendedAttribute,Baseline,F404000,F404001,F404002,F404003,F404004,F404005,F404006,F404007,F404008,F404009," +
            "f40400a,f40400b,f40400c,f40400d,f40400e,f40400f,F404010,F404011,F404012,F404013,F404014,F404015,F404016," +
            "F404017,F404018,F404019,f40401a,f40401b,f40401c,f40401d,f40401e,f40401f,F404020,F404021,F404022,F404023," +
            "F404024,F404025,F404026,F404027,F404028,F404029,f40402a,f40402b,f40402c,f40402d,f40402e,f40402f,F404030," +
            "F404031,F404032,F404033,F404034,F404035,F404036,F404037,F404038,F404039,f40403a,f40403b,f40403c,f40403d," +
            "f40403e,f40403f,F404040,F404041,F404042,F404043,F404044,F404045,F404046,F404047,F404048,F404049,f40404a," +
            "f40404b,f40404c,f40404d,f40404e,f40404f,F404050,F404051,F404052,F404053,F404054,F404055,F404056,F404057," +
            "F404058,F404059,f40405a,f40405b,f40405c,f40405d,f40405e,f40405f,F404060,F404061,F404062,F404063,F404064," +
            "F404065,F404066,F404067,F404068,F404069,f40406a,f40406b,f40406c,f40406d,f40406e,f40406f,F404070,F404071," +
            "F404072,F404073,F404074,F404075,F404076,F404077,F404078,F404079,f40407a,f40407b,f40407c,f40407d,f40407e," +
            "f40407f,F404080,F404081,F404082,F404083,F404084,F404085,F404086,F404087,F404088,F404089,f40408a,f40408b," +
            "f40408c,f40408d,f40408e,f40408f,F404090,F404091,F404092,F404093,F404094,F404095,F404096,F404097,F404098," +
            "F404099,f40409a,f40409b,f40409c,f40409d,f40409e,f40409f,f4040a0,f4040a1,f4040a2,f4040a3,f4040a4,f4040a5," +
            "f4040a6,f4040a7,f4040a8,f4040a9,f4040aa,f4040ab,f4040ac,f4040ad,f4040ae,f4040af,f4040b0,f4040b1,f4040b2," +
            "f4040b3,f4040b4,f4040b5,f4040b6,f4040b7,f4040b8,f4040b9,f4040ba,f4040bb,f4040bc,f4040bd,f4040be,f4040bf," +
            "f4040c0,f4040c1,f4040c2,f4040c3,f4040c4,f4040c5,f4040c6,f4040c7,f4040c8,TimephasedData",
        ["Project/OutlineCodes/OutlineCode/Values"] =
            "Value",
        ["Project/OutlineCodes/OutlineCode/Masks"] =
            "Mask",
        ["Project/ExtendedAttributes/ExtendedAttribute/ValueList"] =
            "Value",
        ["Project/Calendars/Calendar/WeekDays"] =
            "WeekDay",
        ["Project/Calendars/Calendar/Exceptions"] =
            "Exception",
        ["Project/Calendars/Calendar/WorkWeeks"] =
            "WorkWeek",
        ["Project/Tasks/Task/PredecessorLink"] =
            "PredecessorUID,Type,CrossProject,CrossProjectName,LinkLag,LagFormat",
        ["Project/Tasks/Task/ExtendedAttribute"] =
            "FieldID,Value,ValueGUID,DurationFormat",
        ["Project/Tasks/Task/Baseline"] =
            "TimephasedData,Number,Interim,Start,Finish,Duration,DurationFormat,EstimatedDuration,Work,Cost,BCWS,BCWP," +
            "FixedCost",
        ["Project/Tasks/Task/OutlineCode"] =
            "FieldID,ValueID,ValueGUID",
        ["Project/Tasks/Task/TimephasedData"] =
            "Type,UID,Start,Finish,Unit,Value",
        ["Project/Resources/Resource/ExtendedAttribute"] =
            "FieldID,Value,ValueGUID,DurationFormat",
        ["Project/Resources/Resource/Baseline"] =
            "Number,Work,Cost,BCWS,BCWP",
        ["Project/Resources/Resource/OutlineCode"] =
            "FieldID,ValueID,ValueGUID",
        ["Project/Resources/Resource/AvailabilityPeriods"] =
            "AvailabilityPeriod",
        ["Project/Resources/Resource/Rates"] =
            "Rate",
        ["Project/Resources/Resource/TimephasedData"] =
            "Type,UID,Start,Finish,Unit,Value",
        ["Project/Assignments/Assignment/ExtendedAttribute"] =
            "FieldID,Value,ValueGUID,DurationFormat",
        ["Project/Assignments/Assignment/Baseline"] =
            "TimephasedData,Number,Start,Finish,Work,Cost,BCWS,BCWP",
        ["Project/Assignments/Assignment/TimephasedData"] =
            "Type,UID,Start,Finish,Unit,Value",
        ["Project/OutlineCodes/OutlineCode/Values/Value"] =
            "",
        ["Project/OutlineCodes/OutlineCode/Masks/Mask"] =
            "Level,Type,Length,Separator",
        ["Project/ExtendedAttributes/ExtendedAttribute/ValueList/Value"] =
            "",
        ["Project/Calendars/Calendar/WeekDays/WeekDay"] =
            "DayType,DayWorking,TimePeriod,WorkingTimes",
        ["Project/Calendars/Calendar/Exceptions/Exception"] =
            "EnteredByOccurrences,TimePeriod,Occurrences,Name,Type,Period,DaysOfWeek,MonthItem,MonthPosition,Month,MonthDay," +
            "DayWorking,WorkingTimes",
        ["Project/Calendars/Calendar/WorkWeeks/WorkWeek"] =
            "TimePeriod,Name,WeekDays",
        ["Project/Tasks/Task/Baseline/TimephasedData"] =
            "Type,UID,Start,Finish,Unit,Value",
        ["Project/Resources/Resource/AvailabilityPeriods/AvailabilityPeriod"] =
            "AvailableFrom,AvailableTo,AvailableUnits",
        ["Project/Resources/Resource/Rates/Rate"] =
            "RatesFrom,RatesTo,RateTable,StandardRate,StandardRateFormat,OvertimeRate,OvertimeRateFormat,CostPerUse",
        ["Project/Assignments/Assignment/Baseline/TimephasedData"] =
            "Type,UID,Start,Finish,Unit,Value",
        ["Project/Calendars/Calendar/WeekDays/WeekDay/TimePeriod"] =
            "FromDate,ToDate",
        ["Project/Calendars/Calendar/WeekDays/WeekDay/WorkingTimes"] =
            "WorkingTime",
        ["Project/Calendars/Calendar/Exceptions/Exception/TimePeriod"] =
            "FromDate,ToDate",
        ["Project/Calendars/Calendar/Exceptions/Exception/WorkingTimes"] =
            "WorkingTime",
        ["Project/Calendars/Calendar/WorkWeeks/WorkWeek/TimePeriod"] =
            "FromDate,ToDate",
        ["Project/Calendars/Calendar/WorkWeeks/WorkWeek/WeekDays"] =
            "WeekDay",
        ["Project/Calendars/Calendar/WeekDays/WeekDay/WorkingTimes/WorkingTime"] =
            "FromTime,ToTime",
        ["Project/Calendars/Calendar/Exceptions/Exception/WorkingTimes/WorkingTime"] =
            "FromTime,ToTime",
        ["Project/Calendars/Calendar/WorkWeeks/WorkWeek/WeekDays/WeekDay"] =
            "DayType,DayWorking,WorkingTimes",
        ["Project/Calendars/Calendar/WorkWeeks/WorkWeek/WeekDays/WeekDay/WorkingTimes"] =
            "WorkingTime",
        ["Project/Calendars/Calendar/WorkWeeks/WorkWeek/WeekDays/WeekDay/WorkingTimes/WorkingTime"] =
            "FromTime,ToTime",
    };

    private static readonly Dictionary<string, Dictionary<string, int>> Index = Orders.ToDictionary(
        kv => kv.Key,
        kv => kv.Value.Split(',').Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i));

    /// <summary>Sorts every element's children into schema order and removes elements the schema does not define. Returns the removed paths.</summary>
    public static List<string> Normalize(XDocument document)
    {
        var removed = new List<string>();
        if (document.Root is { } root)
            Normalize(root, "Project", removed);
        return removed;
    }

    private static void Normalize(XElement element, string path, List<string> removed)
    {
        if (!Index.TryGetValue(path, out var order))
            return;
        var children = element.Elements().ToList();
        foreach (var child in children)
            child.Remove();
        var kept = children
            .Where(c =>
            {
                if (order.ContainsKey(c.Name.LocalName))
                    return true;
                removed.Add(path + "/" + c.Name.LocalName);
                return false;
            })
            .Select((c, i) => (Child: c, Position: i))
            .OrderBy(x => order[x.Child.Name.LocalName])
            .ThenBy(x => x.Position)
            .Select(x => x.Child)
            .ToList();
        foreach (var child in kept)
        {
            element.Add(child);
            Normalize(child, path + "/" + child.Name.LocalName, removed);
        }
    }
}
