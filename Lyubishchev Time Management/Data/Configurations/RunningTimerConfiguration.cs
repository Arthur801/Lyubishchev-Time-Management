using Lyubishchev_Time_Management.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyubishchev_Time_Management.Data.Configurations;

public class RunningTimerConfiguration : IEntityTypeConfiguration<RunningTimer>
{
    public void Configure(EntityTypeBuilder<RunningTimer> builder)
    {
        builder.ToTable("RunningTimers");

        builder.HasKey(timer => timer.UserId);
        builder.Property(timer => timer.StartedAtUtc).IsRequired();
    }
}
