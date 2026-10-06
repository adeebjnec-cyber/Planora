using System.Xml.Linq;
using Planora.Engine.Calendars;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Persistence;

namespace Planora.Engine.Tests;

public class CalendarPatternTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static WorkingCalendar WithMonthlyMeetingDay()
    {
        var cal = WorkingCalendar.CreateStandard();
        // Every second Friday of the month is a non-working day, three times.
        cal.AddException("Team day", D(1, 1), D(1, 1)).Recurrence = new RecurrencePattern
        {
            Frequency = RecurrenceFrequency.Monthly,
            WeekOfMonth = 2,
            DayOfWeek = DayOfWeek.Friday,
            Occurrences = 3,
        };
        return cal;
    }

    [Fact]
    public void RecurringExceptionCoversEachOccurrenceOnly()
    {
        var cal = WithMonthlyMeetingDay();
        Assert.False(cal.IsWorkingDay(D(1, 9)));
        Assert.False(cal.IsWorkingDay(D(2, 13)));
        Assert.False(cal.IsWorkingDay(D(3, 13)));
        Assert.True(cal.IsWorkingDay(D(4, 10)));   // after the third occurrence
        Assert.True(cal.IsWorkingDay(D(1, 16)));
    }

    [Fact]
    public void WeeklyMultiDayExceptionLastsAsLongAsItsRange()
    {
        var cal = WorkingCalendar.CreateStandard();
        cal.AddException("Shutdown", D(1, 5), D(1, 6)).Recurrence = new RecurrencePattern
        {
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 2,
            DaysOfWeek = { DayOfWeek.Monday },
            End = D(2, 28),
        };
        Assert.False(cal.IsWorkingDay(D(1, 6)));
        Assert.True(cal.IsWorkingDay(D(1, 13)));   // off week
        Assert.False(cal.IsWorkingDay(D(1, 19)));
        Assert.False(cal.IsWorkingDay(D(1, 20)));
        Assert.True(cal.IsWorkingDay(D(1, 21)));
    }

    [Fact]
    public void WorkWeekChangesHoursForItsRange()
    {
        var cal = WorkingCalendar.CreateStandard();
        var summer = new WorkWeek("Summer", D(7, 1), D(8, 31));
        summer.SetDay(DayOfWeek.Friday);
        summer.SetDay(DayOfWeek.Monday, WorkingTime.FromHours(7, 15));
        cal.WorkWeeks.Add(summer);

        Assert.False(cal.IsWorkingDay(D(7, 3)));    // Friday off in summer
        Assert.True(cal.IsWorkingDay(D(6, 26)));    // normal Friday before
        Assert.Equal(8 * 60, cal.GetWorkingTimes(D(7, 6)).Sum(t => (int)(t.To - t.From).TotalMinutes));
        Assert.Equal(cal.GetWorkingTimes(D(6, 30)), cal.GetWorkingTimes(D(7, 7))); // Tuesday unchanged
    }

    [Fact]
    public void ExceptionBeatsWorkWeekAndDerivedCalendarsInheritWorkWeeks()
    {
        var cal = WorkingCalendar.CreateStandard();
        var week = new WorkWeek("Nights", D(3, 1), D(3, 31));
        week.SetDay(DayOfWeek.Saturday, WorkingTime.FromHours(9, 13));
        cal.WorkWeeks.Add(week);
        cal.AddHoliday("Holiday", D(3, 14));

        Assert.True(cal.IsWorkingDay(D(3, 7)));
        Assert.False(cal.IsWorkingDay(D(3, 14)));
        var derived = new WorkingCalendar("Derived", cal);
        Assert.True(derived.IsWorkingDay(D(3, 21)));
    }

    [Fact]
    public void RecurrenceAndWorkWeeksSurviveSaving()
    {
        var p = new Project("P", new DateTime(2026, 1, 5, 8, 0, 0));
        var cal = WithMonthlyMeetingDay();
        var week = new WorkWeek("Summer", D(7, 1), D(8, 31));
        week.SetDay(DayOfWeek.Friday);
        cal.WorkWeeks.Add(week);
        p.Calendar = cal;

        var loaded = ProjectFile.Deserialize(ProjectFile.Serialize(p)).Calendar;
        Assert.False(loaded.IsWorkingDay(D(2, 13)));
        Assert.True(loaded.IsWorkingDay(D(4, 10)));
        Assert.False(loaded.IsWorkingDay(D(7, 3)));

        var xml = MspdiFile.Read(MspdiFile.Write(p)).Calendar;
        Assert.False(xml.IsWorkingDay(D(2, 13)));
        Assert.True(xml.IsWorkingDay(D(4, 10)));
        Assert.False(xml.IsWorkingDay(D(7, 3)));
        Assert.True(xml.IsWorkingDay(D(6, 26)));
    }

    [Fact]
    public void MicrosoftProjectWeeklyRecurringExceptionIsRead()
    {
        var p = new Project("P", new DateTime(2026, 1, 5, 8, 0, 0));
        var doc = MspdiFile.Write(p);
        XNamespace ns = doc.Root!.Name.Namespace;
        var exceptions = doc.Descendants(ns + "Calendar").First().Element(ns + "Exceptions")!;
        exceptions.Add(new XElement(ns + "Exception",
            new XElement(ns + "EnteredByOccurrences", 1),
            new XElement(ns + "TimePeriod",
                new XElement(ns + "FromDate", "2026-01-07T00:00:00"),
                new XElement(ns + "ToDate", "2026-01-28T23:59:00")),
            new XElement(ns + "Occurrences", 4),
            new XElement(ns + "Name", "Wednesdays"),
            new XElement(ns + "Type", 6),
            new XElement(ns + "Period", 1),
            new XElement(ns + "DaysOfWeek", 8),
            new XElement(ns + "DayWorking", 0)));

        var cal = MspdiFile.Read(doc).Calendar;
        Assert.False(cal.IsWorkingDay(D(1, 14)));
        Assert.False(cal.IsWorkingDay(D(1, 28)));
        Assert.True(cal.IsWorkingDay(D(1, 15)));   // the range is not one long holiday
        Assert.True(cal.IsWorkingDay(D(2, 4)));
    }
}
