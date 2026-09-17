using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TimeEntryFlow.Tests.Integration;

public sealed class TagServiceTests
{
    [Fact]
    public async Task Tag_unique_key_rejects_trimmed_case_insensitive_duplicates()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        db.Tags.AddRange(
            TestDatabase.Tag(userId, "Focus", "FOCUS"),
            TestDatabase.Tag(userId, " focus ", "FOCUS"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_case_insensitive_duplicate_but_allows_another_user()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new TagService(db, new TestClock(DateTime.UtcNow));

        Assert.True((await service.CreateAsync(userId, new TagRequest { Name = "Focus" }, default)).Succeeded);
        Assert.Equal("TAG_NAME_CONFLICT", (await service.CreateAsync(userId, new TagRequest { Name = " focus " }, default)).ErrorCode);

        var otherUserId = await TestDatabase.CreateUserAsync(db);
        Assert.True((await service.CreateAsync(otherUserId, new TagRequest { Name = "focus" }, default)).Succeeded);
    }

    [Fact]
    public async Task CreateAsync_trims_name_and_sets_clock_timestamps()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        var now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        await using var db = new AppDbContext(options);
        var service = new TagService(db, new TestClock(now));

        var created = await service.CreateAsync(userId, new TagRequest { Name = " Focus " }, default);

        Assert.True(created.Succeeded);
        Assert.Equal("Focus", created.Tag!.Name);
        Assert.Equal(now, (await db.Tags.SingleAsync()).CreatedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_whitespace_only_name()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new TagService(db, new TestClock(DateTime.UtcNow));

        var result = await service.CreateAsync(userId, new TagRequest { Name = "   " }, default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_NAME", result.ErrorCode);
    }

    [Fact]
    public async Task ListAsync_orders_by_name_case_insensitively()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new TagService(db, new TestClock(DateTime.UtcNow));

        await service.CreateAsync(userId, new TagRequest { Name = "zebra" }, default);
        await service.CreateAsync(userId, new TagRequest { Name = "Apple" }, default);
        await service.CreateAsync(userId, new TagRequest { Name = "banana" }, default);

        var list = await service.ListAsync(userId, default);

        Assert.Equal(["Apple", "banana", "zebra"], list.Select(t => t.Name));
    }

    [Fact]
    public async Task UpdateAsync_renaming_to_an_existing_name_returns_conflict()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new TagService(db, new TestClock(DateTime.UtcNow));

        await service.CreateAsync(userId, new TagRequest { Name = "Focus" }, default);
        var second = await service.CreateAsync(userId, new TagRequest { Name = "Planning" }, default);

        var result = await service.UpdateAsync(userId, second.Tag!.Id, new TagRequest { Name = "focus" }, default);

        Assert.False(result.Succeeded);
        Assert.Equal("TAG_NAME_CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task Update_and_delete_return_not_found_for_another_users_tag()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var otherUserId = await TestDatabase.CreateUserAsync(db);
        var tag = await TestDatabase.CreateTagAsync(db, otherUserId, "Private");
        var service = new TagService(db, new TestClock(DateTime.UtcNow));

        Assert.Equal("TAG_NOT_FOUND", (await service.UpdateAsync(userId, tag.Id, new TagRequest { Name = "Changed" }, default)).ErrorCode);
        Assert.Equal("TAG_NOT_FOUND", (await service.DeleteAsync(userId, tag.Id, default)).ErrorCode);
    }

    [Fact]
    public async Task Concurrent_duplicate_tag_management_creates_yield_exactly_one_success_and_one_row()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;

        async Task<TagResult> CreateFromNewContextAsync()
        {
            await using var db = new AppDbContext(options);
            var service = new TagService(db, new TestClock(DateTime.UtcNow));
            return await service.CreateAsync(userId, new TagRequest { Name = "concurrent-tag" }, default);
        }

        var results = await Task.WhenAll(CreateFromNewContextAsync(), CreateFromNewContextAsync());

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => !r.Succeeded && r.ErrorCode == "TAG_NAME_CONFLICT");

        await using var assertContext = new AppDbContext(options);
        Assert.Equal(1, await assertContext.Tags.CountAsync(t => t.UserId == userId));
    }

    [Fact]
    public async Task Deleting_a_tag_keeps_the_TimeEntry_and_removes_only_the_join_row()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;

        ulong entryId;
        ulong tagId;
        await using (var db = new AppDbContext(options))
        {
            var tagService = new TagService(db, new TestClock(DateTime.UtcNow));
            var createdTag = await tagService.CreateAsync(userId, new TagRequest { Name = "removable" }, default);
            tagId = createdTag.Tag!.Id;

            var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
            var entry = await entryService.CreateAsync(userId, new CreateTimeEntryRequest
            {
                StartTimeUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                EndTimeUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                Tags = ["removable"],
            }, default);
            entryId = entry.Entry!.Id;
        }

        await using (var db = new AppDbContext(options))
        {
            var tagService = new TagService(db, new TestClock(DateTime.UtcNow));
            var result = await tagService.DeleteAsync(userId, tagId, default);
            Assert.True(result.Succeeded);
        }

        await using var assertContext = new AppDbContext(options);
        var entryRow = await assertContext.TimeEntries.Include(e => e.TimeEntryTags).SingleAsync(e => e.Id == entryId);
        Assert.Empty(entryRow.TimeEntryTags);
        Assert.Equal(0, await assertContext.Tags.CountAsync());
    }
}
