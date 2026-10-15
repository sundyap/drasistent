using DrAsistent.Core;
using DrAsistent.Solver;
using Xunit.Abstractions;

namespace DrAsistent.Tests;

public class SolverTests(ITestOutputHelper output)
{
    private static List<Employee> Team()
    {
        var list = new List<Employee>();
        var id = 1;
        for (var i = 0; i < 10; i++) list.Add(new(id++, $"Asistent {i + 1}", StaffCategory.Asistent));
        for (var i = 0; i < 4; i++) list.Add(new(id++, $"Infirmier {i + 1}", StaffCategory.Infirmier));
        for (var i = 0; i < 3; i++) list.Add(new(id++, $"Ingrijitor {i + 1}", StaffCategory.Ingrijitor));
        return list;
    }

    [Fact]
    public void October2026_RespectsHardRules_NormAndEquity()
    {
        var p = new ScheduleProblem
        {
            Year = 2026,
            Month = 10,
            Calendar = new WorkCalendar([]),
            Employees = Team(),
            Leaves =
            [
                // CO luni 12 - vineri 16 -> 136h; duminica 11 nu are voie noapte
                new LeavePeriod(1, LeaveType.CO, new(2026, 10, 12), new(2026, 10, 16)),
                new LeavePeriod(11, LeaveType.CM, new(2026, 10, 19), new(2026, 10, 25)),
            ],
            History = new Dictionary<int, IReadOnlyDictionary<DateOnly, ShiftCode>>
            {
                [2] = new Dictionary<DateOnly, ShiftCode> { [new(2026, 9, 30)] = ShiftCode.N },
            },
        };

        var result = ScheduleSolver.Solve(p, new SolverOptions { MaxSeconds = 30 });
        Assert.True(result.Success, result.Status);
        var s = result.Schedule!;
        output.WriteLine($"{result.Status} {result.WallSeconds:0.0}s");

        foreach (var e in p.Employees)
            output.WriteLine($"{e.Name,-14} {string.Join(" ", s.Row(e.Id).Select(c => c.ToString().PadRight(2)))}");

        var violations = ScheduleValidator.Validate(p, s);
        foreach (var v in violations) output.WriteLine($"{v.Severity} {v.Rule} {v.Date} {v.Message}");
        Assert.DoesNotContain(violations, v => v.Severity == ViolationSeverity.Hard);

        var report = ScheduleValidator.Report(p, s);
        foreach (var r in report)
            output.WriteLine($"{r.Employee.Name,-14} tinta {r.TargetHours} lucrat {r.WorkedHours} speciale {r.SpecialHours} (norm. {r.NormalizedSpecialHours:0.#}) Z8={r.ShortShifts}");

        Assert.All(report, r => Assert.Equal(r.TargetHours, r.WorkedHours));
        Assert.Equal(136, report.Single(r => r.Employee.Id == 1).TargetHours);
        Assert.Equal(ShiftCode.L, s.Get(2, 0)); // repaus dupa noaptea din 30 sept
        Assert.NotEqual(ShiftCode.N, s.Get(1, 10)); // fara noapte duminica 11 inainte de CO

        Assert.DoesNotContain(violations, v => v.Rule.StartsWith("Echitate"));
    }
}


