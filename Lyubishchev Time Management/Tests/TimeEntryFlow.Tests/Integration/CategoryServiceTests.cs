using Lyubishchev_Time_Management.Data;
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
}
