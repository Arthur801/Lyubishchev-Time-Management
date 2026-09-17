using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Models.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TimeEntryFlow.Tests;

internal static class TestDatabase
{
    // Shared-cache SQLite in-memory database: every DbContext built from these options sees the
    // same data, as long as the KeepAlive connection stays open. Needed (instead of the EF Core
    // InMemory provider) so unique-constraint and FK enforcement behave like MySQL.
    public static async Task<(SqliteConnection KeepAlive, DbContextOptions<AppDbContext> Options, ulong UserId)> CreateAsync()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;

        ulong userId;
        await using (var initContext = new AppDbContext(options))
        {
            await initContext.Database.EnsureCreatedAsync();
            userId = await CreateUserAsync(initContext);
        }

        return (keepAlive, options, userId);
    }

    public static async Task<ulong> CreateUserAsync(AppDbContext dbContext)
    {
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-a-real-hash",
            TimeZoneId = "Asia/Taipei",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    public static Category Category(ulong userId, string name, string normalizedName, string color) => new()
    {
        UserId = userId,
        Name = name,
        NormalizedName = normalizedName,
        Color = color,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        User = null!,
    };

    public static async Task<Category> CreateCategoryAsync(AppDbContext dbContext, ulong userId, string name = "Work", string color = "#e5533d")
    {
        var category = Category(userId, name, name.Trim().ToUpperInvariant(), color);
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();
        return category;
    }

    public static Tag Tag(ulong userId, string name, string normalizedName) => new()
    {
        UserId = userId,
        Name = name,
        NormalizedName = normalizedName,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        User = null!,
    };

    public static async Task<Tag> CreateTagAsync(AppDbContext dbContext, ulong userId, string name)
    {
        var tag = Tag(userId, name, name.Trim().ToUpperInvariant());
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync();
        return tag;
    }
}
