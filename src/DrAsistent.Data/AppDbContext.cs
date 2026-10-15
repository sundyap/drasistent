using DrAsistent.Core;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DrAsistent.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();
    public DbSet<LeaveEntity> Leaves => Set<LeaveEntity>();
    public DbSet<HolidayEntity> Holidays => Set<HolidayEntity>();
    public DbSet<CoverageRuleEntity> CoverageRules => Set<CoverageRuleEntity>();
    public DbSet<AppSettingsEntity> Settings => Set<AppSettingsEntity>();
    public DbSet<ScheduleEntity> Schedules => Set<ScheduleEntity>();
    public DbSet<ScheduleCellEntity> ScheduleCells => Set<ScheduleCellEntity>();
    public DbSet<ScheduleSummaryEntity> ScheduleSummaries => Set<ScheduleSummaryEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<EmployeeEntity>(e =>
        {
            e.ToTable("Employees");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<LeaveEntity>(e =>
        {
            e.ToTable("Leaves");
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.EmployeeId, x.Start });
        });

        b.Entity<HolidayEntity>(e =>
        {
            e.ToTable("Holidays");
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Date).IsUnique();
        });

        b.Entity<CoverageRuleEntity>(e =>
        {
            e.ToTable("CoverageRules");
            e.HasData(DefaultCoverage.Rules.Select((r, i) => new CoverageRuleEntity
            {
                Id = i + 1, Shift = r.Shift, Categories = r.Categories, MinRequired = r.MinRequired, Desired = r.Desired,
            }));
        });

        b.Entity<AppSettingsEntity>(e =>
        {
            e.ToTable("Settings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasData(new AppSettingsEntity { Id = 1, ShortShiftCountsForDayCoverage = false, SolverSeconds = 60 });
        });

        b.Entity<ScheduleEntity>(e =>
        {
            e.ToTable("Schedules");
            e.HasIndex(x => new { x.Year, x.Month }).IsUnique();
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
        });

        b.Entity<ScheduleCellEntity>(e =>
        {
            e.ToTable("ScheduleCells");
            e.HasKey(x => new { x.ScheduleId, x.EmployeeId, x.Day });
            e.HasOne<ScheduleEntity>().WithMany(s => s.Cells).HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<EmployeeEntity>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ScheduleSummaryEntity>(e =>
        {
            e.ToTable("ScheduleSummaries");
            e.HasKey(x => new { x.ScheduleId, x.EmployeeId });
            e.HasOne<ScheduleEntity>().WithMany(s => s.Summaries).HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<EmployeeEntity>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Folosit doar de "dotnet ef migrations"; conexiunea reala vine din configurare.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=DrAsistent;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
}

