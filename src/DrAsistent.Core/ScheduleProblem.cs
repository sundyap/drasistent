namespace DrAsistent.Core;

/// <summary>Toate datele necesare pentru generarea/verificarea graficului unei luni.</summary>
public sealed class ScheduleProblem
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required WorkCalendar Calendar { get; init; }
    public required IReadOnlyList<Employee> Employees { get; init; }
    public IReadOnlyList<LeavePeriod> Leaves { get; init; } = [];
    public IReadOnlyList<CoverageRule> Coverage { get; init; } = DefaultCoverage.Rules;

    /// <summary>Ultimele zile din luna anterioara, per angajat (cheie = data). Lipsa = necunoscut (tratat ca liber).</summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<DateOnly, ShiftCode>> History { get; init; } =
        new Dictionary<int, IReadOnlyDictionary<DateOnly, ShiftCode>>();

    /// <summary>Sold de ore reportat din luna anterioara (lucrat - tinta). Pozitiv = a lucrat in plus.</summary>
    public IReadOnlyDictionary<int, int> CarryIn { get; init; } = new Dictionary<int, int>();

    /// <summary>Celule fixate manual de utilizator, respectate la regenerare.</summary>
    public IReadOnlyDictionary<(int EmployeeId, DateOnly Date), ShiftCode> Locked { get; init; } =
        new Dictionary<(int, DateOnly), ShiftCode>();

    /// <summary>Daca tura scurta (07-15) se numara la acoperirea minima de zi.</summary>
    public bool ShortShiftCountsForDayCoverage { get; init; }

    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    public DateOnly Date(int dayIndex) => new(Year, Month, dayIndex + 1);

    public IEnumerable<DateOnly> Days => WorkCalendar.DaysOf(Year, Month);

    public int BaseNormHours => Calendar.BaseNormHours(Year, Month);

    /// <summary>
    /// Codul impus de concediu pentru o zi: CO/CM in zilele lucratoare, L in weekend/sarbatori din interiorul concediului.
    /// null = zi fara concediu.
    /// </summary>
    public ShiftCode? LeaveCode(int employeeId, DateOnly date)
    {
        var leave = Leaves.FirstOrDefault(l => l.EmployeeId == employeeId && l.Contains(date));
        if (leave is null) return null;
        return Calendar.IsWorkingDay(date) ? leave.Type.ToCode() : ShiftCode.L;
    }

    /// <summary>Zile lucratoare de concediu (CO + CM) in luna curenta.</summary>
    public int LeaveWorkingDays(int employeeId) =>
        Days.Count(d => LeaveCode(employeeId, d) is ShiftCode.CO or ShiftCode.CM);

    /// <summary>Ore de lucrat = norma - zile lucratoare de concediu x 8 - sold reportat.</summary>
    public int TargetHours(int employeeId)
    {
        CarryIn.TryGetValue(employeeId, out var carry);
        return Math.Max(0, BaseNormHours - LeaveWorkingDays(employeeId) * 8 - carry);
    }

    public ShiftCode HistoryCode(int employeeId, DateOnly date) =>
        History.TryGetValue(employeeId, out var h) && h.TryGetValue(date, out var c) ? c : ShiftCode.L;

    /// <summary>
    /// Grupurile in care orele de weekend/sarbatori trebuie sa fie echitabile: fiecare categorie,
    /// plus categoriile care se inlocuiesc reciproc la acoperire (ex. infirmiere + ingrijitoare la zi),
    /// altfel weekendurile ar cadea doar pe una dintre ele.
    /// </summary>
    public IReadOnlyList<CategoryMask> EquityGroups
    {
        get
        {
            var groups = Enum.GetValues<StaffCategory>().Select(c => c.ToMask()).ToList();
            foreach (var mask in Coverage.Select(r => r.Categories).Distinct())
                if (!groups.Contains(mask)) groups.Add(mask);
            return groups;
        }
    }

    public bool Counts(CoverageRule rule, ShiftCode code) => rule.Shift switch
    {
        WorkShift.Z => code == ShiftCode.Z || (ShortShiftCountsForDayCoverage && code == ShiftCode.Z8),
        WorkShift.N => code == ShiftCode.N,
        _ => false,
    };
}

/// <summary>Graficul unei luni: pentru fiecare angajat, codul pe fiecare zi (index 0 = ziua 1).</summary>
public sealed class MonthSchedule(int year, int month)
{
    public int Year { get; } = year;
    public int Month { get; } = month;
    public Dictionary<int, ShiftCode[]> Rows { get; } = new();

    public ShiftCode[] Row(int employeeId)
    {
        if (!Rows.TryGetValue(employeeId, out var row))
            Rows[employeeId] = row = new ShiftCode[DateTime.DaysInMonth(Year, Month)];
        return row;
    }

    public ShiftCode Get(int employeeId, int dayIndex) => Rows.TryGetValue(employeeId, out var r) ? r[dayIndex] : ShiftCode.L;
}
