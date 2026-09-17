using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Models.Entities;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Models.Responses;
using Lyubishchev_Time_Management.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record TimeEntryResult(bool Succeeded, TimeEntryResponse? Entry, string? ErrorCode, string? ErrorMessage)
{
    public static TimeEntryResult Ok(TimeEntryResponse entry) => new(true, entry, null, null);

    public static TimeEntryResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed record TimeEntryDeleteResult(bool Succeeded, string? ErrorCode, string? ErrorMessage)
{
    public static TimeEntryDeleteResult Ok() => new(true, null, null);

    public static TimeEntryDeleteResult Fail(string errorCode, string errorMessage) => new(false, errorCode, errorMessage);
}

public sealed class TimeEntryService(AppDbContext dbContext, IClock clock)
{
    private const int MaxTagsPerEntry = 30;

    public async Task<PagedResult<TimeEntryResponse>> ListAsync(
        ulong userId,
        DateTime? rangeStartUtc,
        DateTime? rangeEndUtc,
        ulong? categoryId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = dbContext.TimeEntries.AsNoTracking().Where(e => e.UserId == userId);

        if (rangeStartUtc is not null)
        {
            query = query.Where(e => e.EndTimeUtc > rangeStartUtc.Value);
        }

        if (rangeEndUtc is not null)
        {
            query = query.Where(e => e.StartTimeUtc < rangeEndUtc.Value);
        }

        if (categoryId is not null)
        {
            query = query.Where(e => e.CategoryId == categoryId.Value);
        }

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(e =>
                EF.Functions.Like(e.Name ?? "", $"%{term}%") ||
                e.TimeEntryTags.Any(link => EF.Functions.Like(link.Tag.Name, $"%{term}%")));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(e => e.StartTimeUtc)
            // Cast for the tie-break: SQLite's EF Core provider refuses to translate an ORDER BY
            // on a raw ulong column. long safely covers every realistic TimeEntry.Id.
            .ThenByDescending(e => (long)e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(e => e.Category)
            .Include(e => e.TimeEntryTags)
            .ThenInclude(link => link.Tag)
            .ToListAsync(cancellationToken);

        var items = entries.Select(MapToResponse).ToList();
        return new PagedResult<TimeEntryResponse>(items, page, pageSize, totalCount);
    }

    public async Task<TimeEntryResult> CreateAsync(ulong userId, CreateTimeEntryRequest request, CancellationToken cancellationToken)
    {
        var start = request.StartTimeUtc!.Value;
        var end = request.EndTimeUtc!.Value;
        if (end <= start)
        {
            return TimeEntryResult.Fail("INVALID_TIME_RANGE", "結束時間必須晚於開始時間。");
        }

        Category? category = null;
        if (request.CategoryId is not null)
        {
            category = await GetOwnedCategoryAsync(userId, request.CategoryId.Value, cancellationToken);
            if (category is null)
            {
                return TimeEntryResult.Fail("CATEGORY_NOT_FOUND", "找不到指定的分類。");
            }
        }

        var tagNames = NormalizeTagNames(request.Tags);
        var tags = await FindOrCreateTagsAsync(userId, tagNames, cancellationToken);

        var now = clock.UtcNow;
        var entry = new TimeEntry
        {
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            StartTimeUtc = start,
            EndTimeUtc = end,
            CategoryId = category?.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            User = null!,
        };
        foreach (var tag in tags)
        {
            entry.TimeEntryTags.Add(new TimeEntryTag { TimeEntry = entry, Tag = tag });
        }

        dbContext.TimeEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TimeEntryResult.Ok(BuildResponse(entry, category?.Name, category?.Color, tags.Select(t => t.Name)));
    }

    public async Task<TimeEntryResult> UpdateAsync(ulong userId, ulong id, UpdateTimeEntryRequest request, CancellationToken cancellationToken)
    {
        var entry = await dbContext.TimeEntries
            .Include(e => e.TimeEntryTags)
            .ThenInclude(link => link.Tag)
            .SingleOrDefaultAsync(e => e.Id == id && e.UserId == userId, cancellationToken);
        if (entry is null)
        {
            return TimeEntryResult.Fail("TIME_ENTRY_NOT_FOUND", "找不到這筆紀錄。");
        }

        var effectiveStart = request.StartTimeUtc ?? entry.StartTimeUtc;
        var effectiveEnd = request.EndTimeUtc ?? entry.EndTimeUtc;
        if (effectiveEnd <= effectiveStart)
        {
            return TimeEntryResult.Fail("INVALID_TIME_RANGE", "結束時間必須晚於開始時間。");
        }

        Category? category;
        if (request.CategoryId is not null)
        {
            category = await GetOwnedCategoryAsync(userId, request.CategoryId.Value, cancellationToken);
            if (category is null)
            {
                return TimeEntryResult.Fail("CATEGORY_NOT_FOUND", "找不到指定的分類。");
            }
        }
        else
        {
            category = entry.CategoryId is null
                ? null
                : await dbContext.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == entry.CategoryId.Value, cancellationToken);
        }

        IEnumerable<string> tagNamesForResponse;
        if (request.Tags is not null)
        {
            var tagNames = NormalizeTagNames(request.Tags);
            var tags = await FindOrCreateTagsAsync(userId, tagNames, cancellationToken);
            entry.TimeEntryTags.Clear();
            foreach (var tag in tags)
            {
                entry.TimeEntryTags.Add(new TimeEntryTag { TimeEntry = entry, Tag = tag });
            }

            tagNamesForResponse = tags.Select(t => t.Name);
        }
        else
        {
            tagNamesForResponse = entry.TimeEntryTags.Select(link => link.Tag.Name);
        }

        entry.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        entry.CategoryId = category?.Id;
        entry.StartTimeUtc = effectiveStart;
        entry.EndTimeUtc = effectiveEnd;
        entry.UpdatedAtUtc = clock.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return TimeEntryResult.Ok(BuildResponse(entry, category?.Name, category?.Color, tagNamesForResponse));
    }

    public async Task<TimeEntryDeleteResult> DeleteAsync(ulong userId, ulong id, CancellationToken cancellationToken)
    {
        var affected = await dbContext.TimeEntries
            .Where(e => e.Id == id && e.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return affected > 0
            ? TimeEntryDeleteResult.Ok()
            : TimeEntryDeleteResult.Fail("TIME_ENTRY_NOT_FOUND", "找不到這筆紀錄。");
    }

    public async Task<IReadOnlyList<CategoryOption>> GetCategoryOptionsAsync(ulong userId, CancellationToken cancellationToken)
        => await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryOption(c.Id, c.Name, c.Color))
            .ToListAsync(cancellationToken);

    private async Task<Category?> GetOwnedCategoryAsync(ulong userId, ulong categoryId, CancellationToken cancellationToken)
        => await dbContext.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId, cancellationToken);

    private static List<string> NormalizeTagNames(List<string>? rawNames)
    {
        if (rawNames is null or { Count: 0 })
        {
            return [];
        }

        return rawNames
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Select(name => name.Length > 100 ? name[..100] : name)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTagsPerEntry)
            .ToList();
    }

    private async Task<List<Tag>> FindOrCreateTagsAsync(ulong userId, IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var existing = await dbContext.Tags.Where(t => t.UserId == userId && names.Contains(t.Name)).ToListAsync(cancellationToken);
        var missing = names.Except(existing.Select(t => t.Name)).ToList();
        if (missing.Count == 0)
        {
            return existing;
        }

        var now = clock.UtcNow;
        var newTags = missing
            .Select(name => new Tag { UserId = userId, Name = name, NormalizedName = name.ToUpperInvariant(), CreatedAtUtc = now, UpdatedAtUtc = now, User = null! })
            .ToList();
        dbContext.Tags.AddRange(newTags);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return [.. existing, .. newTags];
        }
        catch (DbUpdateException)
        {
            // Another request for the same user created one of these tag names between our read
            // and write (UserId+Name is a unique index). Detach the failed inserts -- retrying
            // them verbatim would just hit the same unique-constraint violation again -- and
            // re-read: the concurrent writer's rows are now visible and committed.
            foreach (var tag in newTags)
            {
                dbContext.Entry(tag).State = EntityState.Detached;
            }

            var reloaded = await dbContext.Tags.Where(t => t.UserId == userId && names.Contains(t.Name)).ToListAsync(cancellationToken);
            if (names.Except(reloaded.Select(t => t.Name)).Any())
            {
                throw; // Not a name collision -- a real DB error, surface it.
            }

            return reloaded;
        }
    }

    private static TimeEntryResponse MapToResponse(TimeEntry entry) => BuildResponse(
        entry,
        entry.Category?.Name,
        entry.Category?.Color,
        entry.TimeEntryTags.Select(link => link.Tag.Name));

    private static TimeEntryResponse BuildResponse(TimeEntry entry, string? categoryName, string? categoryColor, IEnumerable<string> tagNames) => new(
        entry.Id,
        entry.Name,
        entry.StartTimeUtc,
        entry.EndTimeUtc,
        entry.CategoryId,
        categoryName,
        categoryColor,
        tagNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
        (long)(entry.EndTimeUtc - entry.StartTimeUtc).TotalSeconds);
}
