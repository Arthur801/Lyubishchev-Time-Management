using Lyubishchev_Time_Management.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyubishchev_Time_Management.Data.Configurations;

public class TimeEntryTagConfiguration : IEntityTypeConfiguration<TimeEntryTag>
{
    public void Configure(EntityTypeBuilder<TimeEntryTag> builder)
    {
        builder.ToTable("TimeEntryTags");

        builder.HasKey(link => new { link.TimeEntryId, link.TagId });

        builder.HasOne(link => link.TimeEntry)
            .WithMany(entry => entry.TimeEntryTags)
            .HasForeignKey(link => link.TimeEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(link => link.Tag)
            .WithMany(tag => tag.TimeEntryTags)
            .HasForeignKey(link => link.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
