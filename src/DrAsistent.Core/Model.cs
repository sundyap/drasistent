namespace DrAsistent.Core;

public enum StaffCategory
{
    Asistent = 0,
    Infirmier = 1,
    Ingrijitor = 2,
}

[Flags]
public enum CategoryMask
{
    None = 0,
    Asistent = 1,
    Infirmier = 2,
    Ingrijitor = 4,
}

public enum ShiftCode
{
    L = 0,   // liber
    Z = 1,   // zi 07-19
    N = 2,   // noapte 19-07
    Z8 = 3,  // tura scurta 07-15
    CO = 4,  // concediu de odihna
    CM = 5,  // concediu medical
}

public enum LeaveType
{
    CO = 0,
    CM = 1,
}

/// <summary>Tura de lucru: Z, N sau Z8. Liber/concediu nu e tura.</summary>
public enum WorkShift
{
    Z = 0,
    N = 1,
    Z8 = 2,
}

public static class CategoryExtensions
{
    public static CategoryMask ToMask(this StaffCategory c) => c switch
    {
        StaffCategory.Asistent => CategoryMask.Asistent,
        StaffCategory.Infirmier => CategoryMask.Infirmier,
        StaffCategory.Ingrijitor => CategoryMask.Ingrijitor,
        _ => CategoryMask.None,
    };

    public static bool CanWorkNights(this StaffCategory c) => c != StaffCategory.Ingrijitor;

    public static string Label(this StaffCategory c) => c switch
    {
        StaffCategory.Asistent => "Asistent(ă)",
        StaffCategory.Infirmier => "Infirmier(ă)",
        StaffCategory.Ingrijitor => "Îngrijitor(oare)",
        _ => c.ToString(),
    };

    public static string Label(this CategoryMask m)
    {
        var parts = new List<string>();
        if (m.HasFlag(CategoryMask.Asistent)) parts.Add("Asistente");
        if (m.HasFlag(CategoryMask.Infirmier)) parts.Add("Infirmiere");
        if (m.HasFlag(CategoryMask.Ingrijitor)) parts.Add("Îngrijitoare");
        return string.Join(" + ", parts);
    }
}

public static class ShiftCodeExtensions
{
    public static bool IsWork(this ShiftCode c) => c is ShiftCode.Z or ShiftCode.N or ShiftCode.Z8;

    public static bool IsLeave(this ShiftCode c) => c is ShiftCode.CO or ShiftCode.CM;

    public static int Hours(this ShiftCode c) => c switch
    {
        ShiftCode.Z or ShiftCode.N => 12,
        ShiftCode.Z8 => 8,
        _ => 0,
    };

    public static ShiftCode ToCode(this LeaveType t) => t == LeaveType.CO ? ShiftCode.CO : ShiftCode.CM;
}

public sealed record Employee(int Id, string Name, StaffCategory Category);

public sealed record LeavePeriod(int EmployeeId, LeaveType Type, DateOnly Start, DateOnly End)
{
    public bool Contains(DateOnly d) => d >= Start && d <= End;
}

/// <summary>Cerinta de acoperire pentru o tura (Z sau N) si un grup de categorii.</summary>
/// <param name="MinRequired">Minim obligatoriu (HARD).</param>
/// <param name="Desired">Numar dorit (SOFT), >= MinRequired.</param>
public sealed record CoverageRule(WorkShift Shift, CategoryMask Categories, int MinRequired, int Desired);

public static class DefaultCoverage
{
    public static IReadOnlyList<CoverageRule> Rules { get; } =
    [
        new(WorkShift.Z, CategoryMask.Asistent, 2, 2),
        new(WorkShift.N, CategoryMask.Asistent, 1, 2),
        new(WorkShift.Z, CategoryMask.Infirmier | CategoryMask.Ingrijitor, 2, 2),
        new(WorkShift.N, CategoryMask.Infirmier, 1, 1),
    ];
}
