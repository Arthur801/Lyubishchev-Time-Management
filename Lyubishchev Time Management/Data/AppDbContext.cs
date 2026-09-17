using Lyubishchev_Time_Management.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RunningTimer> RunningTimers => Set<RunningTimer>();

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<TimeEntryTag> TimeEntryTags => Set<TimeEntryTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}
