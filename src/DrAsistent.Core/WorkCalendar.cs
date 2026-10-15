namespace DrAsistent.Core;

/// <summary>
/// Calendarul folosit la calcule: weekend + sarbatori legale.
/// Trebuie sa contina si sarbatorile din prima zi a lunii urmatoare,
/// pentru ca noaptea din ultima zi a lunii are 7 ore in ziua urmatoare.
/// </summary>
public sealed class WorkCalendar(IEnumerable<DateOnly> holidays)
{
    private readonly HashSet<DateOnly> _holidays = [..holidays];

    public bool IsHoliday(DateOnly d) => _holidays.Contains(d);

    public static bool IsWeekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>Zi lucratoare = luni-vineri care nu e sarbatoare legala.</summary>
    public bool IsWorkingDay(DateOnly d) => !IsWeekend(d) && !IsHoliday(d);

    /// <summary>Zi cu spor: sambata, duminica sau sarbatoare legala.</summary>
    public bool IsSpecialDay(DateOnly d) => IsWeekend(d) || IsHoliday(d);

    public static IEnumerable<DateOnly> DaysOf(int year, int month)
    {
        var n = DateTime.DaysInMonth(year, month);
        for (var i = 1; i <= n; i++) yield return new DateOnly(year, month, i);
    }

    public int WorkingDays(int year, int month) => DaysOf(year, month).Count(IsWorkingDay);

    /// <summary>Norma lunara de baza: zile lucratoare x 8 ore.</summary>
    public int BaseNormHours(int year, int month) => WorkingDays(year, month) * 8;

    /// <summary>
    /// Ore de weekend/sarbatoare dintr-o tura care incepe in ziua <paramref name="day"/>.
    /// Z (07-19) = 12h, Z8 (07-15) = 8h daca ziua e speciala.
    /// N (19-07) = 5h din ziua de start (19-24) + 7h din ziua urmatoare (00-07), fiecare segment
    /// numarat doar daca ziua respectiva e speciala.
    /// </summary>
    public int SpecialHours(WorkShift shift, DateOnly day) => shift switch
    {
        WorkShift.Z => IsSpecialDay(day) ? 12 : 0,
        WorkShift.Z8 => IsSpecialDay(day) ? 8 : 0,
        WorkShift.N => (IsSpecialDay(day) ? 5 : 0) + (IsSpecialDay(day.AddDays(1)) ? 7 : 0),
        _ => 0,
    };

    public int SpecialHours(ShiftCode code, DateOnly day) => code switch
    {
        ShiftCode.Z => SpecialHours(WorkShift.Z, day),
        ShiftCode.N => SpecialHours(WorkShift.N, day),
        ShiftCode.Z8 => SpecialHours(WorkShift.Z8, day),
        _ => 0,
    };
}
