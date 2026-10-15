using DrAsistent.Core;
using Google.OrTools.Sat;

namespace DrAsistent.Solver;

public sealed record SolverOptions
{
    public double MaxSeconds { get; init; } = 30;
    public int Workers { get; init; } = 8;
}

public sealed record SolveResult(bool Success, string Status, MonthSchedule? Schedule, double WallSeconds);

/// <summary>
/// Genereaza graficul cu CP-SAT.
/// Reguli HARD ca restrictii: o tura pe zi, concedii, repaus dupa noapte, fara noapte inaintea concediului,
/// fara noapte la ingrijitoare, celule fixate manual.
/// Acoperirea minima e tot obligatorie, dar modelata cu penalizare foarte mare, ca sa obtinem mereu un grafic
/// si sa raportam unde lipseste personal in loc de "nu exista solutie".
/// </summary>
public static class ScheduleSolver
{
    private const long WeightMissingMin = 1_000_000;
    private const long WeightHoursDeviation = 10_000; // per ora
    private const long WeightMissingDesired = 5_000;
    private const long WeightEquityOverTolerance = 20; // per 0.01h peste toleranta
    private const long WeightEquitySpread = 1;          // per 0.01h
    private const long WeightShortShift = 300;
    private const long WeightThreeInARow = 150;
    private const long WeightExcessFirst = 20;
    private const long WeightExcessMore = 200;
    private const long RewardDayThenNight = 100;
    private const long RewardNightThenTwoOff = 100;
    private const long RewardCycleRestart = 50;

    private static readonly WorkShift[] Shifts = [WorkShift.Z, WorkShift.N, WorkShift.Z8];

    public static SolveResult Solve(ScheduleProblem p, SolverOptions? options = null, CancellationToken ct = default)
    {
        options ??= new SolverOptions();
        var model = new CpModel();
        var days = p.DaysInMonth;
        var emps = p.Employees;
        // Faza 1: acoperire, norma, echitate. Faza 2: tiparul Z-N-L-L, fara a strica faza 1.
        var objective = LinearExpr.NewBuilder();
        var patternObjective = LinearExpr.NewBuilder();

        // x[e][d][s] - null cand tura nu e permisa
        var x = new Dictionary<int, BoolVar?[][]>();
        var work = new Dictionary<int, BoolVar[]>();

        foreach (var e in emps)
        {
            var xe = new BoolVar?[days][];
            var we = new BoolVar[days];
            for (var d = 0; d < days; d++)
            {
                var date = p.Date(d);
                xe[d] = new BoolVar?[Shifts.Length];
                we[d] = model.NewBoolVar($"w_{e.Id}_{d}");
                foreach (var s in Shifts)
                {
                    if (s == WorkShift.N && !e.Category.CanWorkNights()) continue;
                    xe[d][(int)s] = model.NewBoolVar($"x_{e.Id}_{d}_{s}");
                }
                model.Add(LinearExpr.Sum(xe[d].Where(v => v is not null)!) == we[d]);

                // Concediu: CO/CM in zile lucratoare, L in weekend/sarbatori din concediu
                if (p.LeaveCode(e.Id, date) is not null)
                {
                    model.Add(we[d] == 0);
                }
                else if (p.Locked.TryGetValue((e.Id, date), out var locked))
                {
                    var shift = ToWorkShift(locked);
                    if (shift is null) model.Add(we[d] == 0);
                    else if (xe[d][(int)shift.Value] is { } lv) model.Add(lv == 1);
                }

                // Fara noapte in ajunul unei zile de concediu
                if (p.LeaveCode(e.Id, date.AddDays(1)) is ShiftCode.CO or ShiftCode.CM && xe[d][(int)WorkShift.N] is { } nv)
                    model.Add(nv == 0);
            }
            x[e.Id] = xe;
            work[e.Id] = we;

            // Repaus dupa noapte (inclusiv noaptea din ultima zi a lunii anterioare)
            if (p.HistoryCode(e.Id, p.Date(0).AddDays(-1)) == ShiftCode.N)
                model.Add(we[0] == 0);
            for (var d = 0; d + 1 < days; d++)
                if (xe[d][(int)WorkShift.N] is { } n)
                    model.Add(n + we[d + 1] <= 1);

            // Ore de lucrat fata de tinta (abaterea se reporteaza in luna urmatoare)
            var hours = LinearExpr.NewBuilder();
            for (var d = 0; d < days; d++)
                foreach (var s in Shifts)
                    if (xe[d][(int)s] is { } v)
                        hours.AddTerm(v, s == WorkShift.Z8 ? 8 : 12);
            var target = p.TargetHours(e.Id);
            var over = model.NewIntVar(0, days * 12, $"over_{e.Id}");
            var under = model.NewIntVar(0, Math.Max(target, 0), $"under_{e.Id}");
            model.Add(hours == target + over - under);
            objective.AddTerm(over, WeightHoursDeviation).AddTerm(under, WeightHoursDeviation);

            foreach (var d in Enumerable.Range(0, days))
                if (xe[d][(int)WorkShift.Z8] is { } z8)
                    objective.AddTerm(z8, WeightShortShift);

            AddPatternTerms(p, model, patternObjective, e, xe, we);
        }

        AddCoverage(p, model, objective, x);
        AddEquity(p, model, objective, x);

        LinearExpr primary = objective;
        model.Minimize(primary);

        var (status, solver) = Run(model, options, options.MaxSeconds * 0.6, ct);
        var wall = solver.WallTime();
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            return new SolveResult(false, status.ToString(), null, wall);

        if (!ct.IsCancellationRequested)
        {
            var best = (long)Math.Round(solver.ObjectiveValue);
            foreach (var v in x.Values.SelectMany(r => r).SelectMany(r => r).OfType<BoolVar>())
                model.AddHint(v, solver.BooleanValue(v) ? 1 : 0);
            model.Add(primary <= best);
            model.Minimize(patternObjective);

            var (status2, solver2) = Run(model, options, options.MaxSeconds * 0.4, ct);
            wall += solver2.WallTime();
            if (status2 is CpSolverStatus.Optimal or CpSolverStatus.Feasible)
            {
                status = status == CpSolverStatus.Optimal && status2 == CpSolverStatus.Optimal ? CpSolverStatus.Optimal : CpSolverStatus.Feasible;
                solver = solver2;
            }
        }

        var schedule = new MonthSchedule(p.Year, p.Month);
        foreach (var e in emps)
        {
            var row = schedule.Row(e.Id);
            for (var d = 0; d < days; d++)
            {
                var date = p.Date(d);
                var leave = p.LeaveCode(e.Id, date);
                if (leave is not null) { row[d] = leave.Value; continue; }
                row[d] = ShiftCode.L;
                foreach (var s in Shifts)
                    if (x[e.Id][d][(int)s] is { } v && solver.BooleanValue(v))
                        row[d] = s switch { WorkShift.Z => ShiftCode.Z, WorkShift.N => ShiftCode.N, _ => ShiftCode.Z8 };
            }
        }
        return new SolveResult(true, status == CpSolverStatus.Optimal ? "Optim" : "Fezabil", schedule, wall);
    }

    private static (CpSolverStatus, CpSolver) Run(CpModel model, SolverOptions options, double seconds, CancellationToken ct)
    {
        var solver = new CpSolver
        {
            StringParameters = $"max_time_in_seconds:{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} num_workers:{options.Workers}",
        };
        using var reg = ct.Register(solver.StopSearch);
        return (solver.Solve(model), solver);
    }

    private static WorkShift? ToWorkShift(ShiftCode c) => c switch
    {
        ShiftCode.Z => WorkShift.Z,
        ShiftCode.N => WorkShift.N,
        ShiftCode.Z8 => WorkShift.Z8,
        _ => null,
    };

    /// <summary>Tiparul Z-N-L-L ca recompense, plus penalizare pentru 3 zile lucrate la rand.</summary>
    private static void AddPatternTerms(ScheduleProblem p, CpModel model, LinearExprBuilder objective,
        Employee e, BoolVar?[][] xe, BoolVar[] we)
    {
        var days = xe.Length;
        var first = p.Date(0);

        // Istoricul din luna anterioara: indici negativi -> constante
        bool HistIs(int d, ShiftCode c) => p.HistoryCode(e.Id, first.AddDays(d)) == c;
        bool HistWorks(int d) => p.HistoryCode(e.Id, first.AddDays(d)).IsWork();

        // Literalele unei conditii; null = conditie imposibila; lista goala = mereu adevarata
        List<ILiteral>? Shift(int d, WorkShift s)
        {
            if (d < 0) return HistIs(d, s == WorkShift.Z ? ShiftCode.Z : ShiftCode.N) ? [] : null;
            if (d >= days) return null;
            return xe[d][(int)s] is { } v ? [v] : null;
        }
        List<ILiteral>? Off(int d)
        {
            if (d < 0) return HistWorks(d) ? null : [];
            if (d >= days) return null;
            return [we[d].Not()];
        }

        void Reward(long weight, string name, params List<ILiteral>?[] conds)
        {
            if (conds.Any(c => c is null)) return;
            var lits = conds.SelectMany(c => c!).ToList();
            if (lits.Count == 0) return;
            var r = model.NewBoolVar(name);
            foreach (var l in lits) model.AddImplication(r, l);
            objective.AddTerm(r, -weight);
        }

        for (var d = -3; d < days; d++)
        {
            if (e.Category.CanWorkNights())
            {
                // conditiile care implica doar istoricul nu pot fi influentate
                if (d + 1 >= 0) Reward(RewardDayThenNight, $"zn_{e.Id}_{d}", Shift(d, WorkShift.Z), Shift(d + 1, WorkShift.N));
                if (d + 2 >= 0) Reward(RewardNightThenTwoOff, $"nll_{e.Id}_{d}", Shift(d, WorkShift.N), Off(d + 1), Off(d + 2));
                if (d + 3 >= 0) Reward(RewardCycleRestart, $"cyc_{e.Id}_{d}", Shift(d, WorkShift.N), Off(d + 2), Shift(d + 3, WorkShift.Z));
            }
        }

        for (var d = 0; d + 2 < days; d++)
        {
            var three = model.NewBoolVar($"three_{e.Id}_{d}");
            model.Add(we[d] + we[d + 1] + we[d + 2] - 2 <= three);
            objective.AddTerm(three, WeightThreeInARow);
        }
    }

    private static void AddCoverage(ScheduleProblem p, CpModel model, LinearExprBuilder objective,
        Dictionary<int, BoolVar?[][]> x)
    {
        for (var d = 0; d < p.DaysInMonth; d++)
        {
            foreach (var (rule, ri) in p.Coverage.Select((r, i) => (r, i)))
            {
                var vars = new List<BoolVar>();
                foreach (var e in p.Employees.Where(e => rule.Categories.HasFlag(e.Category.ToMask())))
                {
                    if (x[e.Id][d][(int)rule.Shift] is { } v) vars.Add(v);
                    if (rule.Shift == WorkShift.Z && p.ShortShiftCountsForDayCoverage && x[e.Id][d][(int)WorkShift.Z8] is { } z8)
                        vars.Add(z8);
                }
                var count = LinearExpr.Sum(vars);
                var desired = Math.Max(rule.Desired, rule.MinRequired);

                var missMin = model.NewIntVar(0, rule.MinRequired, $"missMin_{d}_{ri}");
                var missDesired = model.NewIntVar(0, desired - rule.MinRequired, $"missDes_{d}_{ri}");
                model.Add(count + missMin >= rule.MinRequired);
                model.Add(count + missMin + missDesired >= desired);
                objective.AddTerm(missMin, WeightMissingMin).AddTerm(missDesired, WeightMissingDesired);

                // Personal peste necesar: impartit uniform pe zile, de preferat in zilele lucratoare
                // (orele de weekend/sarbatori se platesc cu spor)
                var excessFactor = p.Calendar.IsSpecialDay(p.Date(d)) ? 3 : 1;
                var excessFirst = model.NewIntVar(0, 1, $"exA_{d}_{ri}");
                var excessMore = model.NewIntVar(0, Math.Max(vars.Count, 1), $"exB_{d}_{ri}");
                model.Add(count - desired <= excessFirst + excessMore);
                objective.AddTerm(excessFirst, WeightExcessFirst * excessFactor).AddTerm(excessMore, WeightExcessMore * excessFactor);
            }
        }
    }

    /// <summary>
    /// Echitate ore weekend/sarbatori in fiecare grup (categorie sau categorii interschimbabile). Orele se raporteaza la norma intreaga
    /// (cine are concediu e comparat proportional), in sutimi de ora.
    /// </summary>
    private static void AddEquity(ScheduleProblem p, CpModel model, LinearExprBuilder objective,
        Dictionary<int, BoolVar?[][]> x)
    {
        var baseNorm = p.BaseNormHours;
        foreach (var mask in p.EquityGroups)
        {
            var members = p.Employees.Where(e => mask.HasFlag(e.Category.ToMask()) && p.TargetHours(e.Id) > 0).ToList();
            if (members.Count < 2) continue;

            var normalized = new List<LinearExpr>();
            long maxValue = 0;
            foreach (var e in members)
            {
                var factor = (long)Math.Round(100.0 * baseNorm / p.TargetHours(e.Id));
                var special = LinearExpr.NewBuilder();
                long total = 0;
                for (var d = 0; d < p.DaysInMonth; d++)
                    foreach (var s in Shifts)
                    {
                        var h = p.Calendar.SpecialHours(s, p.Date(d));
                        if (h > 0 && x[e.Id][d][(int)s] is { } v)
                        {
                            special.AddTerm(v, h * factor);
                            total += h * factor;
                        }
                    }
                maxValue = Math.Max(maxValue, total);
                normalized.Add(special);
            }

            var hi = model.NewIntVar(0, maxValue, $"eqHi_{(int)mask}");
            var lo = model.NewIntVar(0, maxValue, $"eqLo_{(int)mask}");
            foreach (var n in normalized)
            {
                model.Add(hi >= n);
                model.Add(lo <= n);
            }
            var overTol = model.NewIntVar(0, maxValue, $"eqOver_{(int)mask}");
            model.Add(hi - lo - ScheduleValidator.EquityToleranceHours * 100 <= overTol);
            objective.AddTerm(overTol, WeightEquityOverTolerance);
            objective.Add((hi - lo) * WeightEquitySpread);
        }
    }
}


