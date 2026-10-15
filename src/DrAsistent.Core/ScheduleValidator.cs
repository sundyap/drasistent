namespace DrAsistent.Core;

public enum ViolationSeverity
{
    Hard,
    Soft,
}

/// <param name="EmployeeId">null pentru probleme la nivel de sectie (acoperire).</param>
/// <param name="Date">null pentru probleme la nivel de luna (ore, echitate).</param>
public sealed record Violation(ViolationSeverity Severity, string Rule, string Message, int? EmployeeId = null, DateOnly? Date = null);

public sealed record EmployeeReport(
    Employee Employee,
    int BaseNormHours,
    int LeaveWorkingDays,
    int CarryIn,
    int TargetHours,
    int WorkedHours,
    int SpecialHours,
    int DayShifts,
    int NightShifts,
    int ShortShifts)
{
    public int Balance => WorkedHours - TargetHours;

    /// <summary>Ore speciale raportate la norma intreaga, ca sa compar corect angajatii cu concediu.</summary>
    public double NormalizedSpecialHours =>
        TargetHours == 0 ? 0 : (double)SpecialHours * BaseNormHours / TargetHours;
}

public static class ScheduleValidator
{
    public const int EquityToleranceHours = 2;

    public static IReadOnlyList<EmployeeReport> Report(ScheduleProblem p, MonthSchedule s)
    {
        var result = new List<EmployeeReport>();
        foreach (var e in p.Employees)
        {
            int worked = 0, special = 0, z = 0, n = 0, z8 = 0;
            for (var i = 0; i < p.DaysInMonth; i++)
            {
                var code = s.Get(e.Id, i);
                worked += code.Hours();
                special += p.Calendar.SpecialHours(code, p.Date(i));
                switch (code)
                {
                    case ShiftCode.Z: z++; break;
                    case ShiftCode.N: n++; break;
                    case ShiftCode.Z8: z8++; break;
                }
            }
            p.CarryIn.TryGetValue(e.Id, out var carry);
            result.Add(new EmployeeReport(e, p.BaseNormHours, p.LeaveWorkingDays(e.Id), carry,
                p.TargetHours(e.Id), worked, special, z, n, z8));
        }
        return result;
    }

    public static IReadOnlyList<Violation> Validate(ScheduleProblem p, MonthSchedule s)
    {
        var v = new List<Violation>();
        var days = p.DaysInMonth;

        foreach (var e in p.Employees)
        {
            for (var i = 0; i < days; i++)
            {
                var date = p.Date(i);
                var code = s.Get(e.Id, i);
                var prev = i == 0 ? p.HistoryCode(e.Id, date.AddDays(-1)) : s.Get(e.Id, i - 1);

                if (code == ShiftCode.N && !e.Category.CanWorkNights())
                    v.Add(new(ViolationSeverity.Hard, "Doar zi la îngrijitoare", $"{e.Name}: tură de noapte nepermisă", e.Id, date));

                var leave = p.LeaveCode(e.Id, date);
                if (leave is ShiftCode.CO or ShiftCode.CM && code != leave)
                    v.Add(new(ViolationSeverity.Hard, "Calcul CO și CM", $"{e.Name}: în concediu ({leave}) dar are {code}", e.Id, date));
                else if (leave == ShiftCode.L && code.IsWork())
                    v.Add(new(ViolationSeverity.Hard, "Calcul CO și CM", $"{e.Name}: lucrează în weekend/sărbătoare din concediu", e.Id, date));
                else if (leave is null && code.IsLeave())
                    v.Add(new(ViolationSeverity.Hard, "Calcul CO și CM", $"{e.Name}: {code} fără concediu înregistrat", e.Id, date));

                if (prev == ShiftCode.N && code.IsWork())
                    v.Add(new(ViolationSeverity.Hard, "Repaus după noapte", $"{e.Name}: {code} imediat după noapte", e.Id, date));

                if (code == ShiftCode.N && p.LeaveCode(e.Id, date.AddDays(1)) is ShiftCode.CO or ShiftCode.CM)
                    v.Add(new(ViolationSeverity.Hard, "Restricție concedii", $"{e.Name}: noapte în ajunul concediului", e.Id, date));
            }
        }

        for (var i = 0; i < days; i++)
        {
            var date = p.Date(i);
            foreach (var rule in p.Coverage)
            {
                var count = p.Employees.Count(e => rule.Categories.HasFlag(e.Category.ToMask()) && p.Counts(rule, s.Get(e.Id, i)));
                var what = $"{rule.Categories.Label()} tura {rule.Shift}";
                if (count < rule.MinRequired)
                    v.Add(new(ViolationSeverity.Hard, "Acoperire minimă", $"{what}: {count}/{rule.MinRequired}", null, date));
                else if (count < rule.Desired)
                    v.Add(new(ViolationSeverity.Soft, "Acoperire dorită", $"{what}: {count}/{rule.Desired}", null, date));
            }
        }

        var report = Report(p, s);
        foreach (var r in report.Where(r => r.Balance != 0))
            v.Add(new(ViolationSeverity.Soft, "Normă", $"{r.Employee.Name}: {r.WorkedHours}h lucrate față de {r.TargetHours}h ({r.Balance:+0;-0}h, se reportează)", r.Employee.Id));

        foreach (var mask in p.EquityGroups)
        {
            var group = report.Where(r => r.TargetHours > 0 && mask.HasFlag(r.Employee.Category.ToMask())).ToList();
            if (group.Count < 2) continue;
            var max = group.Max(r => r.NormalizedSpecialHours);
            var min = group.Min(r => r.NormalizedSpecialHours);
            if (max - min > EquityToleranceHours + 0.5)
                v.Add(new(ViolationSeverity.Soft, "Echitate weekend/sărbători",
                    $"{mask.Label()}: diferență de {max - min:0.#}h între angajați (toleranță ±{EquityToleranceHours}h)"));
        }

        return v;
    }
}
