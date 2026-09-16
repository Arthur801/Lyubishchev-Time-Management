using Lyubishchev_Time_Management.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyubishchev_Time_Management.Data.Configurations;

public class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("TimeEntries", table =>
            table.HasCheckConstraint("CK_TimeEntries_EndTimeUtc_After_StartTimeUtc", "`EndTimeUtc` > `StartTimeUtc`"));

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedOnAdd();
        builder.Property(entry => entry.Name).HasMaxLength(500);
        builder.Property(entry => entry.StartTimeUtc).IsRequired();
        builder.Property(entry => entry.EndTimeUtc).IsRequired();
        builder.Property(entry => entry.CreatedAtUtc).IsRequired();
        builder.Property(entry => entry.UpdatedAtUtc).IsRequired();

        builder.HasIndex(entry => new { entry.UserId, entry.StartTimeUtc });
        builder.HasIndex(entry => new { entry.UserId, entry.CategoryId, entry.StartTimeUtc });

        builder.HasOne(entry => entry.User)
            .WithMany(user => user.TimeEntries)
            .HasForeignKey(entry => entry.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(entry => entry.Category)
            .WithMany(category => category.TimeEntries)
            .HasForeignKey(entry => entry.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
