using Lyubishchev_Time_Management.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyubishchev_Time_Management.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedOnAdd();
        builder.Property(category => category.Name).HasMaxLength(100).IsRequired();
        builder.Property(category => category.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(category => category.Color).HasMaxLength(7).IsRequired();
        builder.Property(category => category.CreatedAtUtc).IsRequired();
        builder.Property(category => category.UpdatedAtUtc).IsRequired();
        builder.HasIndex(category => new { category.UserId, category.NormalizedName }).IsUnique();

        builder.HasOne(category => category.User)
            .WithMany(user => user.Categories)
            .HasForeignKey(category => category.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
