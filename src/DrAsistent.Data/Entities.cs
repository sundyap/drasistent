using DrAsistent.Core;
using Microsoft.AspNetCore.Identity;

namespace DrAsistent.Data;

public class ApplicationUser : IdentityUser;

public class EmployeeEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public StaffCategory Category { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class LeaveEntity
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public EmployeeEntity? Employee { get; set; }
    public LeaveType Type { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public string? Note { get; set; }
}

public class HolidayEntity
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}

public class CoverageRuleEntity
{
    public int Id { get; set; }
    public WorkShift Shift { get; set; }
    public CategoryMask Categories { get; set; }
    public int MinRequired { get; set; }
    public int Desired { get; set; }
}

public class AppSettingsEntity
{
    public int Id { get; set; } = 1;
    public bool ShortShiftCountsForDayCoverage { get; set; }
    public int SolverSeconds { get; set; } = 60;
}

public class ScheduleEntity
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public List<ScheduleCellEntity> Cells { get; set; } = [];
    public List<ScheduleSummaryEntity> Summaries { get; set; } = [];
}

public class ScheduleCellEntity
{
    public int ScheduleId { get; set; }
    public int EmployeeId { get; set; }
    public int Day { get; set; }
    public ShiftCode Code { get; set; }
    /// <summary>Fixata manual: se pastreaza la regenerare.</summary>
    public bool Locked { get; set; }
}

/// <summary>Totalurile salvate odata cu graficul; soldul (lucrat - tinta) se reporteaza in luna urmatoare.</summary>
public class ScheduleSummaryEntity
{
    public int ScheduleId { get; set; }
    public int EmployeeId { get; set; }
    public int CarryIn { get; set; }
    public int TargetHours { get; set; }
    public int WorkedHours { get; set; }
    public int SpecialHours { get; set; }
}
