using DrAsistent.Core;
using DrAsistent.Data;
using DrAsistent.Export;
using DrAsistent.Solver;
using Microsoft.EntityFrameworkCore;

namespace DrAsistent.Web.Services;

/// <summary>Graficul unei luni asa cum e incarcat in pagina: problema + celulele + ce e fixat manual.</summary>
public sealed class ScheduleState(ScheduleProblem problem, MonthSchedule schedule, HashSet<(int EmployeeId, int Day)> locked, DateTime? savedAt)
{
    public ScheduleProblem Problem { get; set; } = problem;
    public MonthSchedule Schedule { get; set; } = schedule;
    public HashSet<(int EmployeeId, int Day)> Locked { get; } = locked;
    public DateTime? SavedAt { get; set; } = savedAt;
}

public sealed class ScheduleService(IDbContextFactory<AppDbContext> dbFactory, IConfiguration config)
{
    public string DepartmentName => config["Department:Name"] ?? "Grafic de lucru";

    public async Task<ScheduleState> LoadAsync(int year, int month)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var saved = await db.Schedules.AsNoTracking().Include(s => s.Cells)
            .FirstOrDefaultAsync(s => s.Year == year && s.Month == month);
        var savedEmployeeIds = saved?.Cells.Select(c => c.EmployeeId).ToHashSet() ?? [];

        var problem = await BuildProblemAsync(db, year, month, savedEmployeeIds);
        var schedule = new MonthSchedule(year, month);
        var locked = new HashSet<(int, int)>();
        foreach (var e in problem.Employees)
        {
            var row = schedule.Row(e.Id);
            for (var d = 0; d < row.Length; d++)
                row[d] = problem.LeaveCode(e.Id, problem.Date(d)) ?? ShiftCode.L;
        }
        if (saved is not null)
        {
            foreach (var c in saved.Cells.Where(c => schedule.Rows.ContainsKey(c.EmployeeId) && c.Day >= 1 && c.Day <= problem.DaysInMonth))
            {
                // concediile introduse dupa salvare au prioritate
                if (problem.LeaveCode(c.EmployeeId, problem.Date(c.Day - 1)) is null)
                    schedule.Row(c.EmployeeId)[c.Day - 1] = c.Code.IsLeave() ? ShiftCode.L : c.Code;
                if (c.Locked) locked.Add((c.EmployeeId, c.Day - 1));
            }
        }
        return new ScheduleState(problem, schedule, locked, saved?.UpdatedAt);
    }

    public async Task<int> SolverSecondsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.Settings.AsNoTracking().FirstOrDefaultAsync())?.SolverSeconds ?? 60;
    }

    public async Task<SolveResult> GenerateAsync(ScheduleState state, CancellationToken ct)
    {
        var seconds = await SolverSecondsAsync();
        var locked = state.Locked.ToDictionary(
            l => (l.EmployeeId, state.Problem.Date(l.Day)),
            l => state.Schedule.Get(l.EmployeeId, l.Day));
        var problem = WithLocked(state.Problem, locked);
        return await Task.Run(() => ScheduleSolver.Solve(problem, new SolverOptions
        {
            MaxSeconds = seconds,
            Workers = Math.Max(1, Environment.ProcessorCount),
        }, ct), ct);
    }

    private static ScheduleProblem WithLocked(ScheduleProblem p, IReadOnlyDictionary<(int, DateOnly), ShiftCode> locked) => new()
    {
        Year = p.Year,
        Month = p.Month,
        Calendar = p.Calendar,
        Employees = p.Employees,
        Leaves = p.Leaves,
        Coverage = p.Coverage,
        History = p.History,
        CarryIn = p.CarryIn,
        ShortShiftCountsForDayCoverage = p.ShortShiftCountsForDayCoverage,
        Locked = locked,
    };

    public async Task SaveAsync(ScheduleState state, string? user)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var p = state.Problem;
        var entity = await db.Schedules.Include(s => s.Cells).Include(s => s.Summaries)
            .FirstOrDefaultAsync(s => s.Year == p.Year && s.Month == p.Month);
        if (entity is null)
        {
            entity = new ScheduleEntity { Year = p.Year, Month = p.Month };
            db.Schedules.Add(entity);
        }
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = user;
        entity.Cells.Clear();
        entity.Summaries.Clear();

        foreach (var e in p.Employees)
            for (var d = 0; d < p.DaysInMonth; d++)
                entity.Cells.Add(new ScheduleCellEntity
                {
                    EmployeeId = e.Id,
                    Day = d + 1,
                    Code = state.Schedule.Get(e.Id, d),
                    Locked = state.Locked.Contains((e.Id, d)),
                });

        foreach (var r in ScheduleValidator.Report(p, state.Schedule))
            entity.Summaries.Add(new ScheduleSummaryEntity
            {
                EmployeeId = r.Employee.Id,
                CarryIn = r.CarryIn,
                TargetHours = r.TargetHours,
                WorkedHours = r.WorkedHours,
                SpecialHours = r.SpecialHours,
            });

        await db.SaveChangesAsync();
        state.SavedAt = entity.UpdatedAt;
    }

    public async Task<ScheduleDocument?> DocumentAsync(int year, int month)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Schedules.AnyAsync(s => s.Year == year && s.Month == month)) return null;
        var state = await LoadAsync(year, month);
        return new ScheduleDocument(DepartmentName, state.Problem, state.Schedule);
    }

    private static async Task<ScheduleProblem> BuildProblemAsync(AppDbContext db, int year, int month, HashSet<int> savedEmployeeIds)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive || savedEmployeeIds.Contains(e.Id))
            .OrderBy(e => e.Category).ThenBy(e => e.SortOrder).ThenBy(e => e.Name)
            .Select(e => new Employee(e.Id, e.Name, e.Category))
            .ToListAsync();

        // +1 zi: noaptea din ultima zi a lunii depinde de ziua urmatoare (sarbatoare, concediu)
        var leaves = await db.Leaves.AsNoTracking()
            .Where(l => l.Start <= last.AddDays(1) && l.End >= first)
            .Select(l => new LeavePeriod(l.EmployeeId, l.Type, l.Start, l.End))
            .ToListAsync();

        var holidays = await db.Holidays.AsNoTracking()
            .Where(h => h.Date >= first && h.Date <= last.AddDays(1))
            .Select(h => h.Date)
            .ToListAsync();

        var coverage = await db.CoverageRules.AsNoTracking()
            .Select(r => new CoverageRule(r.Shift, r.Categories, r.MinRequired, r.Desired))
            .ToListAsync();

        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync() ?? new AppSettingsEntity();

        // Continuitate: ultimele 3 zile si soldul de ore din luna anterioara
        var prevFirst = first.AddMonths(-1);
        var prev = await db.Schedules.AsNoTracking()
            .Include(s => s.Cells).Include(s => s.Summaries)
            .FirstOrDefaultAsync(s => s.Year == prevFirst.Year && s.Month == prevFirst.Month);

        var history = new Dictionary<int, IReadOnlyDictionary<DateOnly, ShiftCode>>();
        var carry = new Dictionary<int, int>();
        if (prev is not null)
        {
            var prevDays = DateTime.DaysInMonth(prevFirst.Year, prevFirst.Month);
            foreach (var g in prev.Cells.Where(c => c.Day > prevDays - 3).GroupBy(c => c.EmployeeId))
                history[g.Key] = g.ToDictionary(c => new DateOnly(prevFirst.Year, prevFirst.Month, c.Day), c => c.Code);
            foreach (var s in prev.Summaries)
                carry[s.EmployeeId] = s.WorkedHours - s.TargetHours;
        }

        return new ScheduleProblem
        {
            Year = year,
            Month = month,
            Calendar = new WorkCalendar(holidays),
            Employees = employees,
            Leaves = leaves,
            Coverage = coverage.Count > 0 ? coverage : DefaultCoverage.Rules,
            History = history,
            CarryIn = carry,
            ShortShiftCountsForDayCoverage = settings.ShortShiftCountsForDayCoverage,
        };
    }
}
