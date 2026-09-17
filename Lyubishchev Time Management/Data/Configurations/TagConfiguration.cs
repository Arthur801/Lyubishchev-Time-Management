using Lyubishchev_Time_Management.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyubishchev_Time_Management.Data.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");

        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Id).ValueGeneratedOnAdd();
        builder.Property(tag => tag.Name).HasMaxLength(100).IsRequired();
        builder.Property(tag => tag.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(tag => tag.CreatedAtUtc).IsRequired();
        builder.Property(tag => tag.UpdatedAtUtc).IsRequired();
        builder.HasIndex(tag => new { tag.UserId, tag.NormalizedName }).IsUnique();

        builder.HasOne(tag => tag.User)
            .WithMany(user => user.Tags)
            .HasForeignKey(tag => tag.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
