using System.Xml.Linq;
using Planora.Engine.Interop;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.Engine.Tests;

public class MspdiFileTests
{
    private const int Day = 480;

    private static DateTime D(int day, int hour = 8) => new(2026, 1, day, hour, 0, 0);

    private static Project Sample()
    {
        var p = new Project("Website", D(5));
        p.Calendar.AddHoliday("Holiday", new DateOnly(2026, 1, 7));
        p.StatusDate = D(6, 17);
        var phase = p.AddTask("Build");
        var design = p.AddTask("Design", 2 * Day);
        var code = p.AddTask("Code", 3 * Day);
        p.Indent(design);
        p.Indent(code);
        var launch = p.AddTask("Launch", 0);
        launch.IsMilestone = true;
        launch.Deadline = D(20, 17);
        var review = p.AddTask("Review", Day);
        review.ConstraintType = ConstraintType.StartNoEarlierThan;
        review.ConstraintDate = D(12);
        review.IsElapsedDuration = false;
        review.Notes = "Check accessibility";
        p.Link(design, code, LinkType.StartToStart, Day);
        p.Link(phase, launch);
        var percentLink = p.Link(code, review, LinkType.FinishToStart);
        percentLink.LagFormat = LagFormat.PercentOfPredecessor;
        percentLink.LagPercent = -50;

        var dev = p.AddResource("Developer");
        dev.StandardRate = 50;
        dev.CostPerUse = 25;
        dev.AddRateChange(0, new CostRate(D(9, 0), 60));
        dev.Calendar!.AddHoliday("Leave", new DateOnly(2026, 1, 9));
        var paint = p.AddResource("Paint", ResourceType.Material);
        paint.MaterialLabel = "litres";
        paint.StandardRate = 4;
        var fee = p.AddResource("Fee", ResourceType.Cost);
        p.Assign(code, dev);
        p.Assign(code, paint, 12);
        p.Assign(review, fee).CostAmount = 300;
        design.PercentComplete = 50;
        design.ActualStart = D(5);
        design.FixedCost = 99.5m;
        ProjectScheduler.Schedule(p);
        p.SaveBaseline();
        return p;
    }

    [Fact]
    public void RoundTripKeepsTheSchedule()
    {
        var original = Sample();
        var loaded = MspdiFile.Read(MspdiFile.Write(original));
        ProjectScheduler.Schedule(loaded);

        Assert.Equal(original.Tasks.Select(t => t.Name), loaded.Tasks.Select(t => t.Name));
        foreach (var (o, l) in original.Tasks.Zip(loaded.Tasks))
        {
            Assert.Equal((o.Id, o.OutlineLevel), (l.Id, l.OutlineLevel));
            Assert.Equal((o.Start, o.Finish), (l.Start, l.Finish));
            Assert.Equal(o.Cost, l.Cost);
            Assert.Equal(o.Baselines[0]?.Finish, l.Baselines[0]?.Finish);
        }
        Assert.Equal(original.Finish, loaded.Finish);
        Assert.Equal(original.Cost, loaded.Cost);
    }

    [Fact]
    public void RoundTripKeepsDetails()
    {
        var loaded = MspdiFile.Read(MspdiFile.Write(Sample()));

        Assert.Equal("Website", loaded.Name);
        Assert.Equal(D(6, 17), loaded.StatusDate);
        Assert.False(loaded.Calendar.IsWorkingDay(new DateOnly(2026, 1, 7)));

        var review = loaded.Tasks.Single(t => t.Name == "Review");
        Assert.Equal(ConstraintType.StartNoEarlierThan, review.ConstraintType);
        Assert.Equal("Check accessibility", review.Notes);
        Assert.Equal(LagFormat.PercentOfPredecessor, review.Predecessors[0].LagFormat);
        Assert.Equal(-50, review.Predecessors[0].LagPercent);
        Assert.Equal(300m, review.Assignments.Single().CostAmount);

        var code = loaded.Tasks.Single(t => t.Name == "Code");
        Assert.Equal(LinkType.StartToStart, code.Predecessors[0].Type);
        Assert.Equal(Day, code.Predecessors[0].LagMinutes);
        Assert.Equal(12, code.Assignments.Single(a => a.Resource.Name == "Paint").Units);

        var dev = loaded.Resources.Single(r => r.Name == "Developer");
        Assert.Equal(50m, dev.StandardRate);
        Assert.Equal(25m, dev.CostPerUse);
        Assert.Equal(2, dev.CostRateTables[0].Count);
        Assert.False(dev.Calendar!.IsWorkingDay(new DateOnly(2026, 1, 9)));
        Assert.Same(loaded.Calendar, dev.Calendar.BaseCalendar);

        var launch = loaded.Tasks.Single(t => t.Name == "Launch");
        Assert.True(launch.IsMilestone);
        Assert.Equal(D(20, 17), launch.Deadline);

        var design = loaded.Tasks.Single(t => t.Name == "Design");
        Assert.Equal(50, design.PercentComplete);
        Assert.Equal(99.5m, design.FixedCost);
        Assert.NotNull(design.Baselines[0]);
    }

    [Fact]
    public void ReadsMicrosoftProjectStyleFile()
    {
        // Shape follows Microsoft Project's XML export: project summary task UID 0, blank rows,
        // older day-type-0 exceptions, unassigned assignment rows, costs in cents.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Project xmlns="http://schemas.microsoft.com/project">
              <Title>Office Move</Title>
              <ScheduleFromStart>1</ScheduleFromStart>
              <StartDate>2026-01-05T08:00:00</StartDate>
              <CalendarUID>1</CalendarUID>
              <MinutesPerDay>480</MinutesPerDay>
              <Calendars>
                <Calendar>
                  <UID>1</UID><Name>Standard</Name><IsBaseCalendar>1</IsBaseCalendar><BaseCalendarUID>-1</BaseCalendarUID>
                  <WeekDays>
                    <WeekDay><DayType>1</DayType><DayWorking>0</DayWorking></WeekDay>
                    <WeekDay><DayType>2</DayType><DayWorking>1</DayWorking><WorkingTimes>
                      <WorkingTime><FromTime>08:00:00</FromTime><ToTime>12:00:00</ToTime></WorkingTime>
                      <WorkingTime><FromTime>13:00:00</FromTime><ToTime>17:00:00</ToTime></WorkingTime></WorkingTimes></WeekDay>
                    <WeekDay><DayType>0</DayType><DayWorking>0</DayWorking>
                      <TimePeriod><FromDate>2026-01-06T00:00:00</FromDate><ToDate>2026-01-06T23:59:00</ToDate></TimePeriod></WeekDay>
                  </WeekDays>
                </Calendar>
                <Calendar><UID>2</UID><Name>Mover</Name><IsBaseCalendar>0</IsBaseCalendar><BaseCalendarUID>1</BaseCalendarUID></Calendar>
              </Calendars>
              <Tasks>
                <Task><UID>0</UID><ID>0</ID><Name>Office Move</Name><OutlineLevel>0</OutlineLevel><Summary>1</Summary></Task>
                <Task><UID>1</UID><ID>1</ID><Name>Pack</Name><OutlineLevel>1</OutlineLevel><Duration>PT16H0M0S</Duration><DurationFormat>7</DurationFormat>
                  <FixedCost>12345</FixedCost><ConstraintType>0</ConstraintType></Task>
                <Task><UID>2</UID><ID>2</ID><IsNull>1</IsNull></Task>
                <Task><UID>3</UID><ID>3</ID><Name>Drive</Name><OutlineLevel>1</OutlineLevel><Duration>PT24H0M0S</Duration><DurationFormat>8</DurationFormat>
                  <PredecessorLink><PredecessorUID>1</PredecessorUID><Type>1</Type><CrossProject>0</CrossProject><LinkLag>2400</LinkLag><LagFormat>5</LagFormat></PredecessorLink></Task>
              </Tasks>
              <Resources>
                <Resource><UID>0</UID><ID>0</ID><Type>1</Type><IsNull>0</IsNull></Resource>
                <Resource><UID>1</UID><ID>1</ID><Name>Mover</Name><Type>1</Type><MaxUnits>2.00</MaxUnits><StandardRate>40</StandardRate>
                  <CostPerUse>5000</CostPerUse><AccrueAt>3</AccrueAt><CalendarUID>2</CalendarUID></Resource>
              </Resources>
              <Assignments>
                <Assignment><UID>1</UID><TaskUID>1</TaskUID><ResourceUID>1</ResourceUID><Units>1</Units><Work>PT16H0M0S</Work></Assignment>
                <Assignment><UID>2</UID><TaskUID>3</TaskUID><ResourceUID>-65535</ResourceUID><Units>1</Units></Assignment>
              </Assignments>
            </Project>
            """;

        var p = MspdiFile.Read(XDocument.Parse(xml));
        ProjectScheduler.Schedule(p);

        Assert.Equal("Office Move", p.Name);
        Assert.Equal(new[] { "Pack", "Drive" }, p.Tasks.Select(t => t.Name));
        var pack = p.Tasks[0];
        var drive = p.Tasks[1];
        Assert.Equal(123.45m, pack.FixedCost);
        Assert.True(drive.IsElapsedDuration);
        Assert.Equal(240, drive.Predecessors.Single().LagMinutes);
        Assert.False(p.Calendar.IsWorkingDay(new DateOnly(2026, 1, 6)));

        // Mon 5 Jan, then Tue 6 is a holiday: Pack finishes Wed 7 Jan 17:00.
        Assert.Equal(new DateTime(2026, 1, 7, 17, 0, 0), pack.Finish);
        var mover = p.Resources.Single();
        Assert.Equal(50m, mover.CostPerUse);
        Assert.Equal(2.0, mover.MaxUnits);
        Assert.Single(pack.Assignments);
        Assert.Empty(drive.Assignments);
        Assert.Equal(16 * 40m + 50m + 123.45m, pack.Cost);
    }

    [Fact]
    public void RejectsNonProjectXml()
    {
        Assert.Throws<InvalidDataException>(() => MspdiFile.Read(XDocument.Parse("<Something/>")));
    }
}
