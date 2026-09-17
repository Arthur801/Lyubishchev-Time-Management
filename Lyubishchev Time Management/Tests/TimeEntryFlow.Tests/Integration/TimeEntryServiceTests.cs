using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TimeEntryFlow.Tests.Integration;

public sealed class TimeEntryServiceTests
{
    private static Task<(Microsoft.Data.Sqlite.SqliteConnection KeepAlive, DbContextOptions<AppDbContext> Options, ulong UserId)> CreateSharedDatabaseAsync()
        => TestDatabase.CreateAsync();

    private static Task<ulong> CreateUserAsync(AppDbContext dbContext) => TestDatabase.CreateUserAsync(dbContext);

    private static async Task<ulong> CreateCategoryAsync(AppDbContext dbContext, ulong userId, string name = "Work", string color = "#e5533d")
        => (await TestDatabase.CreateCategoryAsync(dbContext, userId, name, color)).Id;

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_allows_overlapping_time_entries_for_the_same_user()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        var first = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
        }, CancellationToken.None);

        var second = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 30),
            EndTimeUtc = Utc(2026, 1, 1, 10, 30),
        }, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, await dbContext.TimeEntries.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_end_not_after_start()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        var result = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 10, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
        }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_TIME_RANGE", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_categoryId_owned_by_another_user()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);

        var otherUserId = await CreateUserAsync(dbContext);
        var otherUsersCategoryId = await CreateCategoryAsync(dbContext, otherUserId);

        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));
        var result = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
            CategoryId = otherUsersCategoryId,
        }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CATEGORY_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_creates_a_new_tag_and_reuses_an_existing_tag_by_name()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        var first = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
            Tags = ["deep-work"],
        }, CancellationToken.None);

        var second = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 11, 0),
            EndTimeUtc = Utc(2026, 1, 1, 12, 0),
            Tags = ["deep-work", "planning"],
        }, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(["deep-work"], first.Entry!.Tags);
        Assert.Equal(["deep-work", "planning"], second.Entry!.Tags.OrderBy(t => t));
        Assert.Equal(2, await dbContext.Tags.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_tag_find_or_create_survives_a_concurrent_duplicate_tag_creation()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        var clock = new TestClock(DateTime.UtcNow);

        async Task<TimeEntryResult> CreateFromNewContextAsync(int startHour)
        {
            await using var dbContext = new AppDbContext(options);
            var service = new TimeEntryService(dbContext, clock);
            return await service.CreateAsync(userId, new CreateTimeEntryRequest
            {
                StartTimeUtc = Utc(2026, 1, 1, startHour, 0),
                EndTimeUtc = Utc(2026, 1, 1, startHour + 1, 0),
                Tags = ["concurrent-tag"],
            }, CancellationToken.None);
        }

        var results = await Task.WhenAll(CreateFromNewContextAsync(9), CreateFromNewContextAsync(11));

        Assert.All(results, r => Assert.True(r.Succeeded));

        await using var assertContext = new AppDbContext(options);
        var tags = await assertContext.Tags.Where(t => t.UserId == userId && t.Name == "concurrent-tag").ToListAsync();
        Assert.Single(tags);

        var links = await assertContext.TimeEntryTags.Where(l => l.TagId == tags[0].Id).ToListAsync();
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task CreateAsync_inline_tag_reuses_the_normalized_key()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        var first = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
            Tags = ["Focus"],
        }, CancellationToken.None);

        var second = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 11, 0),
            EndTimeUtc = Utc(2026, 1, 1, 12, 0),
            Tags = [" focus "],
        }, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(["Focus"], first.Entry!.Tags);
        Assert.Equal(["Focus"], second.Entry!.Tags);

        var tags = await dbContext.Tags.Where(t => t.UserId == userId).ToListAsync();
        var tag = Assert.Single(tags);
        Assert.Equal("Focus", tag.Name);

        var links = await dbContext.TimeEntryTags.Where(l => l.TagId == tag.Id).ToListAsync();
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task UpdateAsync_merges_a_partial_change_and_still_enforces_end_after_start()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        var created = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
        }, CancellationToken.None);

        var invalid = await service.UpdateAsync(userId, created.Entry!.Id, new UpdateTimeEntryRequest
        {
            EndTimeUtc = Utc(2026, 1, 1, 8, 0),
        }, CancellationToken.None);
        Assert.False(invalid.Succeeded);
        Assert.Equal("INVALID_TIME_RANGE", invalid.ErrorCode);

        var valid = await service.UpdateAsync(userId, created.Entry.Id, new UpdateTimeEntryRequest
        {
            EndTimeUtc = Utc(2026, 1, 1, 11, 0),
        }, CancellationToken.None);
        Assert.True(valid.Succeeded);
        Assert.Equal(Utc(2026, 1, 1, 9, 0), valid.Entry!.StartTimeUtc);
        Assert.Equal(Utc(2026, 1, 1, 11, 0), valid.Entry.EndTimeUtc);
    }

    [Fact]
    public async Task UpdateAsync_rejects_updating_an_entry_owned_by_another_user()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);

        var otherUserId = await CreateUserAsync(dbContext);
        var otherService = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));
        var otherEntry = await otherService.CreateAsync(otherUserId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
        }, CancellationToken.None);

        var result = await otherService.UpdateAsync(userId, otherEntry.Entry!.Id, new UpdateTimeEntryRequest
        {
            Name = "hijacked",
        }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("TIME_ENTRY_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_omitting_tags_leaves_existing_tags_untouched()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;

        ulong entryId;
        await using (var createContext = new AppDbContext(options))
        {
            var createService = new TimeEntryService(createContext, new TestClock(DateTime.UtcNow));
            var created = await createService.CreateAsync(userId, new CreateTimeEntryRequest
            {
                StartTimeUtc = Utc(2026, 1, 1, 9, 0),
                EndTimeUtc = Utc(2026, 1, 1, 10, 0),
                Tags = ["a", "b"],
            }, CancellationToken.None);
            entryId = created.Entry!.Id;
        }

        // A fresh DbContext (matching the real per-request scope) so this exercises the actual
        // Include chain, rather than relying on an already-tracked Tag entity from the Create
        // call above to fix up the TimeEntryTags.Tag navigation for free.
        await using var updateContext = new AppDbContext(options);
        var updateService = new TimeEntryService(updateContext, new TestClock(DateTime.UtcNow));
        var result = await updateService.UpdateAsync(userId, entryId, new UpdateTimeEntryRequest
        {
            Name = "renamed",
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["a", "b"], result.Entry!.Tags.OrderBy(t => t));
    }

    [Fact]
    public async Task UpdateAsync_replacing_tags_with_an_empty_list_clears_all_tags()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        var created = await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
            Tags = ["a", "b"],
        }, CancellationToken.None);

        var result = await service.UpdateAsync(userId, created.Entry!.Id, new UpdateTimeEntryRequest
        {
            Tags = [],
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Entry!.Tags);
    }

    [Fact]
    public async Task DeleteAsync_only_deletes_the_authenticated_users_own_entry()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);

        var otherUserId = await CreateUserAsync(dbContext);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));
        var otherEntry = await service.CreateAsync(otherUserId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
        }, CancellationToken.None);

        var rejected = await service.DeleteAsync(userId, otherEntry.Entry!.Id, CancellationToken.None);
        Assert.False(rejected.Succeeded);
        Assert.Equal("TIME_ENTRY_NOT_FOUND", rejected.ErrorCode);
        Assert.Equal(1, await dbContext.TimeEntries.CountAsync());

        var accepted = await service.DeleteAsync(otherUserId, otherEntry.Entry.Id, CancellationToken.None);
        Assert.True(accepted.Succeeded);
        Assert.Equal(0, await dbContext.TimeEntries.CountAsync());
    }

    [Fact]
    public async Task Deleting_a_category_sets_TimeEntry_CategoryId_to_null_and_keeps_the_entry()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;

        ulong entryId;
        ulong categoryId;
        await using (var dbContext = new AppDbContext(options))
        {
            categoryId = await CreateCategoryAsync(dbContext, userId);
            var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));
            var created = await service.CreateAsync(userId, new CreateTimeEntryRequest
            {
                StartTimeUtc = Utc(2026, 1, 1, 9, 0),
                EndTimeUtc = Utc(2026, 1, 1, 10, 0),
                CategoryId = categoryId,
            }, CancellationToken.None);
            entryId = created.Entry!.Id;
        }

        await using (var dbContext = new AppDbContext(options))
        {
            var category = await dbContext.Categories.SingleAsync(c => c.Id == categoryId);
            dbContext.Categories.Remove(category);
            await dbContext.SaveChangesAsync();
        }

        await using var assertContext = new AppDbContext(options);
        var entry = await assertContext.TimeEntries.SingleAsync(e => e.Id == entryId);
        Assert.Null(entry.CategoryId);
    }

    [Fact]
    public async Task Deleting_a_tag_removes_only_the_join_row_and_keeps_the_entry()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;

        ulong entryId;
        await using (var dbContext = new AppDbContext(options))
        {
            var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));
            var created = await service.CreateAsync(userId, new CreateTimeEntryRequest
            {
                StartTimeUtc = Utc(2026, 1, 1, 9, 0),
                EndTimeUtc = Utc(2026, 1, 1, 10, 0),
                Tags = ["removable"],
            }, CancellationToken.None);
            entryId = created.Entry!.Id;
        }

        await using (var dbContext = new AppDbContext(options))
        {
            var tag = await dbContext.Tags.SingleAsync(t => t.UserId == userId && t.Name == "removable");
            dbContext.Tags.Remove(tag);
            await dbContext.SaveChangesAsync();
        }

        await using var assertContext = new AppDbContext(options);
        var entry = await assertContext.TimeEntries.Include(e => e.TimeEntryTags).SingleAsync(e => e.Id == entryId);
        Assert.Empty(entry.TimeEntryTags);
        Assert.Equal(0, await assertContext.Tags.CountAsync());
    }

    [Fact]
    public async Task ListAsync_includes_an_entry_overlapping_the_range_boundary_and_excludes_one_outside_it()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        // Crosses midnight from Jan 1 into Jan 2.
        await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 23, 0),
            EndTimeUtc = Utc(2026, 1, 2, 1, 0),
        }, CancellationToken.None);

        // Fully outside the Jan 2 query range.
        await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 10, 0),
            EndTimeUtc = Utc(2026, 1, 1, 11, 0),
        }, CancellationToken.None);

        var result = await service.ListAsync(
            userId, Utc(2026, 1, 2, 0, 0), Utc(2026, 1, 3, 0, 0), categoryId: null, search: null, page: 1, pageSize: 50, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(Utc(2026, 1, 1, 23, 0), item.StartTimeUtc);
    }

    [Fact]
    public async Task ListAsync_paginates_and_reports_an_accurate_total_count()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        for (var hour = 0; hour < 5; hour++)
        {
            await service.CreateAsync(userId, new CreateTimeEntryRequest
            {
                StartTimeUtc = Utc(2026, 1, 1, hour, 0),
                EndTimeUtc = Utc(2026, 1, 1, hour, 30),
            }, CancellationToken.None);
        }

        var firstPage = await service.ListAsync(userId, null, null, null, null, page: 1, pageSize: 2, CancellationToken.None);
        var lastPage = await service.ListAsync(userId, null, null, null, null, page: 3, pageSize: 2, CancellationToken.None);

        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(5, firstPage.TotalCount);
        Assert.Single(lastPage.Items);
    }

    [Fact]
    public async Task ListAsync_filters_by_category_and_by_search_text_matching_name_or_tag()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var categoryId = await CreateCategoryAsync(dbContext, userId, "Work");
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 9, 0),
            EndTimeUtc = Utc(2026, 1, 1, 10, 0),
            Name = "Deep work session",
            CategoryId = categoryId,
            Tags = ["focus"],
        }, CancellationToken.None);

        await service.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 11, 0),
            EndTimeUtc = Utc(2026, 1, 1, 12, 0),
            Name = "Grocery run",
            Tags = ["errand"],
        }, CancellationToken.None);

        var byCategory = await service.ListAsync(userId, null, null, categoryId, null, 1, 50, CancellationToken.None);
        Assert.Single(byCategory.Items);
        Assert.Equal("Deep work session", byCategory.Items[0].Name);

        var byNameSearch = await service.ListAsync(userId, null, null, null, "grocery", 1, 50, CancellationToken.None);
        Assert.Single(byNameSearch.Items);
        Assert.Equal("Grocery run", byNameSearch.Items[0].Name);

        var byTagSearch = await service.ListAsync(userId, null, null, null, "focus", 1, 50, CancellationToken.None);
        Assert.Single(byTagSearch.Items);
        Assert.Equal("Deep work session", byTagSearch.Items[0].Name);
    }

    [Fact]
    public async Task ListAsync_orders_newest_to_oldest()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = new TimeEntryService(dbContext, new TestClock(DateTime.UtcNow));

        await service.CreateAsync(userId, new CreateTimeEntryRequest { StartTimeUtc = Utc(2026, 1, 1, 9, 0), EndTimeUtc = Utc(2026, 1, 1, 10, 0), Name = "oldest" }, CancellationToken.None);
        await service.CreateAsync(userId, new CreateTimeEntryRequest { StartTimeUtc = Utc(2026, 1, 2, 9, 0), EndTimeUtc = Utc(2026, 1, 2, 10, 0), Name = "middle" }, CancellationToken.None);
        await service.CreateAsync(userId, new CreateTimeEntryRequest { StartTimeUtc = Utc(2026, 1, 3, 9, 0), EndTimeUtc = Utc(2026, 1, 3, 10, 0), Name = "newest" }, CancellationToken.None);

        var result = await service.ListAsync(userId, null, null, null, null, 1, 50, CancellationToken.None);

        Assert.Equal(["newest", "middle", "oldest"], result.Items.Select(i => i.Name));
    }
}
