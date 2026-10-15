using DrAsistent.Core;

namespace DrAsistent.Tests;

public class CalendarTests
{
    private static readonly WorkCalendar NoHolidays = new([]);

    [Fact]
    public void October2026_Has22WorkingDays_176Hours()
    {
        Assert.Equal(22, NoHolidays.WorkingDays(2026, 10));
        Assert.Equal(176, NoHolidays.BaseNormHours(2026, 10));
    }

    [Fact]
    public void Holiday_OnWeekday_ReducesNorm()
    {
        // 30 nov 2026 = luni, 1 dec 2026 = marti
        var cal = new WorkCalendar([new DateOnly(2026, 11, 30)]);
        Assert.Equal(NoHolidays.BaseNormHours(2026, 11) - 8, cal.BaseNormHours(2026, 11));
    }

    [Theory]
    [InlineData("2026-10-09", WorkShift.N, 7)]   // vineri noapte: 00-07 sambata
    [InlineData("2026-10-10", WorkShift.Z, 12)]  // sambata zi
    [InlineData("2026-10-10", WorkShift.N, 12)]  // sambata noapte
    [InlineData("2026-10-11", WorkShift.Z, 12)]  // duminica zi
    [InlineData("2026-10-11", WorkShift.N, 5)]   // duminica noapte: 19-24
    [InlineData("2026-10-08", WorkShift.N, 0)]   // joi noapte
    [InlineData("2026-10-08", WorkShift.Z, 0)]
    [InlineData("2026-10-10", WorkShift.Z8, 8)]
    public void SpecialHours_Weekend(string date, WorkShift shift, int expected)
    {
        Assert.Equal(expected, NoHolidays.SpecialHours(shift, DateOnly.Parse(date)));
    }

    [Fact]
    public void SpecialHours_HolidayOnWednesday()
    {
        var cal = new WorkCalendar([new DateOnly(2026, 4, 15)]); // miercuri
        Assert.Equal(7, cal.SpecialHours(WorkShift.N, new DateOnly(2026, 4, 14)));
        Assert.Equal(12, cal.SpecialHours(WorkShift.Z, new DateOnly(2026, 4, 15)));
        Assert.Equal(5, cal.SpecialHours(WorkShift.N, new DateOnly(2026, 4, 15)));
    }

    [Theory]
    [InlineData(2024, 5, 5)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 12)]
    [InlineData(2027, 5, 2)]
    public void OrthodoxEaster(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), RomanianHolidays.OrthodoxEaster(year));
    }
}

public class NormTests
{
    private static ScheduleProblem Problem(params LeavePeriod[] leaves) => new()
    {
        Year = 2026,
        Month = 10,
        Calendar = new WorkCalendar([]),
        Employees = [new Employee(1, "A", StaffCategory.Asistent)],
        Leaves = leaves,
    };

    [Fact]
    public void FiveWorkingDaysOfLeave_Gives136()
    {
        // 12-16 oct 2026 = luni-vineri
        var p = Problem(new LeavePeriod(1, LeaveType.CO, new(2026, 10, 12), new(2026, 10, 16)));
        Assert.Equal(5, p.LeaveWorkingDays(1));
        Assert.Equal(136, p.TargetHours(1));
    }

    [Fact]
    public void LeaveOverWeekend_ConsumesOnlyWorkingDays()
    {
        // 12-18 oct: luni-duminica -> 5 zile lucratoare, weekendul ramane L
        var p = Problem(new LeavePeriod(1, LeaveType.CM, new(2026, 10, 12), new(2026, 10, 18)));
        Assert.Equal(5, p.LeaveWorkingDays(1));
        Assert.Equal(ShiftCode.CM, p.LeaveCode(1, new(2026, 10, 16)));
        Assert.Equal(ShiftCode.L, p.LeaveCode(1, new(2026, 10, 17)));
    }

    [Fact]
    public void CarryIn_AdjustsTarget()
    {
        var p = new ScheduleProblem
        {
            Year = 2026, Month = 10, Calendar = new WorkCalendar([]),
            Employees = [new Employee(1, "A", StaffCategory.Asistent)],
            CarryIn = new Dictionary<int, int> { [1] = -4 },
        };
        Assert.Equal(180, p.TargetHours(1));
    }
}
