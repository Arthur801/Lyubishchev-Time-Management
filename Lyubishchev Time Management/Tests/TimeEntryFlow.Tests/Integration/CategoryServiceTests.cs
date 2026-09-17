using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TimeEntryFlow.Tests.Integration;

public sealed class CategoryServiceTests
{
    [Fact]
    public async Task Category_unique_key_rejects_trimmed_case_insensitive_duplicates()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        db.Categories.AddRange(
            TestDatabase.Category(userId, "Focus", "FOCUS", "#e5533d"),
            TestDatabase.Category(userId, " focus ", "FOCUS", "#2957c8"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CreateAsync_trims_name_sets_clock_timestamps_and_rejects_duplicate_key()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        await using var db = new AppDbContext(options);
        var service = new CategoryService(db, new TestClock(now));

        var created = await service.CreateAsync(userId, new CategoryRequest { Name = " Focus ", Color = "#e5533d" }, default);
        var duplicate = await service.CreateAsync(userId, new CategoryRequest { Name = "focus", Color = "#2957c8" }, default);

        Assert.True(created.Succeeded);
        Assert.Equal("Focus", created.Category!.Name);
        Assert.Equal(now, (await db.Categories.SingleAsync()).CreatedAtUtc);
        Assert.Equal("CATEGORY_NAME_CONFLICT", duplicate.ErrorCode);
    }

    [Fact]
    public async Task Update_and_delete_return_not_found_for_another_users_category()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var otherId = await TestDatabase.CreateUserAsync(db);
        var category = await TestDatabase.CreateCategoryAsync(db, otherId, "Private");
        var service = new CategoryService(db, new TestClock(DateTime.UtcNow));

        Assert.Equal("CATEGORY_NOT_FOUND", (await service.UpdateAsync(userId, category.Id, new CategoryRequest { Name = "Changed", Color = "#000000" }, default)).ErrorCode);
        Assert.Equal("CATEGORY_NOT_FOUND", (await service.DeleteAsync(userId, category.Id, default)).ErrorCode);
    }

    [Fact]
    public async Task ListAsync_orders_by_name_case_insensitively()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new CategoryService(db, new TestClock(DateTime.UtcNow));

        await service.CreateAsync(userId, new CategoryRequest { Name = "zebra", Color = "#111111" }, default);
        await service.CreateAsync(userId, new CategoryRequest { Name = "Apple", Color = "#222222" }, default);
        await service.CreateAsync(userId, new CategoryRequest { Name = "banana", Color = "#333333" }, default);

        var list = await service.ListAsync(userId, default);

        Assert.Equal(["Apple", "banana", "zebra"], list.Select(c => c.Name));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_whitespace_only_name()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new CategoryService(db, new TestClock(DateTime.UtcNow));

        var result = await service.CreateAsync(userId, new CategoryRequest { Name = "   ", Color = "#e5533d" }, default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_NAME", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_on_own_category_changes_name_and_color_but_preserves_CreatedAtUtc()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var db = new AppDbContext(options);
        var service = new CategoryService(db, new TestClock(createdAt));

        var created = await service.CreateAsync(userId, new CategoryRequest { Name = "Work", Color = "#111111" }, default);

        var updateService = new CategoryService(db, new TestClock(updatedAt));
        var updated = await updateService.UpdateAsync(userId, created.Category!.Id, new CategoryRequest { Name = "Deep Work", Color = "#222222" }, default);

        Assert.True(updated.Succeeded);
        Assert.Equal("Deep Work", updated.Category!.Name);
        Assert.Equal("#222222", updated.Category.Color);

        var row = await db.Categories.SingleAsync(c => c.Id == created.Category.Id);
        Assert.Equal(createdAt, row.CreatedAtUtc);
        Assert.Equal(updatedAt, row.UpdatedAtUtc);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_callers_own_category()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new CategoryService(db, new TestClock(DateTime.UtcNow));
        var created = await service.CreateAsync(userId, new CategoryRequest { Name = "Temp", Color = "#111111" }, default);

        var result = await service.DeleteAsync(userId, created.Category!.Id, default);

        Assert.True(result.Succeeded);
        Assert.Equal(0, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task Different_users_may_each_have_a_category_with_the_same_name()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var otherUserId = await TestDatabase.CreateUserAsync(db);
        var service = new CategoryService(db, new TestClock(DateTime.UtcNow));

        var first = await service.CreateAsync(userId, new CategoryRequest { Name = "Focus", Color = "#111111" }, default);
        var second = await service.CreateAsync(otherUserId, new CategoryRequest { Name = "Focus", Color = "#222222" }, default);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task Deleting_a_category_assigned_to_a_TimeEntry_leaves_the_entry_uncategorized()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;

        ulong entryId;
        ulong categoryId;
        await using (var db = new AppDbContext(options))
        {
            var categoryService = new CategoryService(db, new TestClock(DateTime.UtcNow));
            var created = await categoryService.CreateAsync(userId, new CategoryRequest { Name = "Work", Color = "#111111" }, default);
            categoryId = created.Category!.Id;

            var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
            var entry = await entryService.CreateAsync(userId, new CreateTimeEntryRequest
            {
                StartTimeUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                EndTimeUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                CategoryId = categoryId,
            }, default);
            entryId = entry.Entry!.Id;
        }

        await using (var db = new AppDbContext(options))
        {
            var categoryService = new CategoryService(db, new TestClock(DateTime.UtcNow));
            var result = await categoryService.DeleteAsync(userId, categoryId, default);
            Assert.True(result.Succeeded);
        }

        await using var assertContext = new AppDbContext(options);
        var entryRow = await assertContext.TimeEntries.SingleAsync(e => e.Id == entryId);
        Assert.Null(entryRow.CategoryId);
    }
}
