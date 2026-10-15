using System.Globalization;
using DrAsistent.Core;

namespace DrAsistent.Export;

/// <summary>Date comune pentru exporturile Excel si PDF.</summary>
public sealed class ScheduleDocument(string title, ScheduleProblem problem, MonthSchedule schedule)
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public string Title { get; } = title;
    public ScheduleProblem Problem { get; } = problem;
    public MonthSchedule Schedule { get; } = schedule;
    public IReadOnlyList<EmployeeReport> Reports { get; } = ScheduleValidator.Report(problem, schedule);

    public string MonthName => Ro.TextInfo.ToTitleCase(Ro.DateTimeFormat.GetMonthName(Problem.Month));

    public string Subtitle => $"{MonthName} {Problem.Year} · normă {Problem.BaseNormHours}h ({Problem.Calendar.WorkingDays(Problem.Year, Problem.Month)} zile lucrătoare)";

    public static string DayLetter(DateOnly d) => d.DayOfWeek switch
    {
        DayOfWeek.Monday => "L",
        DayOfWeek.Tuesday => "Ma",
        DayOfWeek.Wednesday => "Mi",
        DayOfWeek.Thursday => "J",
        DayOfWeek.Friday => "V",
        DayOfWeek.Saturday => "S",
        _ => "D",
    };

    /// <summary>Angajatii grupati pe categorii, in ordinea din grafic.</summary>
    public IEnumerable<IGrouping<StaffCategory, EmployeeReport>> Groups =>
        Reports.GroupBy(r => r.Employee.Category).OrderBy(g => g.Key);

    public static string CellText(ShiftCode c) => c == ShiftCode.L ? "" : c.ToString();

    /// <summary>Culori de fundal (hex fara #) pentru fiecare cod.</summary>
    public static string? CellColor(ShiftCode c) => c switch
    {
        ShiftCode.Z => "FFF4CC",
        ShiftCode.Z8 => "FFE699",
        ShiftCode.N => "BDD7EE",
        ShiftCode.CO => "C6EFCE",
        ShiftCode.CM => "F8CBAD",
        _ => null,
    };

    public const string SpecialDayColor = "EDEDED";
}
