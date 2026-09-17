using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Models.Entities;
using Lyubishchev_Time_Management.Models.Responses;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record TimerResult(bool Succeeded, TimerResponse? Timer, string? ErrorCode, string? ErrorMessage)
{
    public static TimerResult Ok(TimerResponse timer) => new(true, timer, null, null);

    public static TimerResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed record StopTimerResult(bool Succeeded, TimeEntryResponse? TimeEntry, string? ErrorCode, string? ErrorMessage)
{
    public static StopTimerResult Ok(TimeEntryResponse timeEntry) => new(true, timeEntry, null, null);

    public static StopTimerResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed class TimerService(AppDbContext dbContext, IClock clock, TimeEntryService entryService)
{
    public async Task<TimerResponse> GetStatusAsync(ulong userId, CancellationToken cancellationToken)
    {
        var runningTimer = await dbContext.RunningTimers
            .AsNoTracking()
            .SingleOrDefaultAsync(timer => timer.UserId == userId, cancellationToken);

        return runningTimer is null
            ? new TimerResponse(false, null)
            : new TimerResponse(true, runningTimer.StartedAtUtc);
    }

    public async Task<TimerResult> StartAsync(ulong userId, CancellationToken cancellationToken)
    {
        var alreadyRunning = await dbContext.RunningTimers.AnyAsync(timer => timer.UserId == userId, cancellationToken);
        if (alreadyRunning)
        {
            return TimerResult.Fail("TIMER_ALREADY_RUNNING", "計時器已經在執行中。");
        }

        var startedAtUtc = clock.UtcNow;
        dbContext.RunningTimers.Add(new RunningTimer { UserId = userId, StartedAtUtc = startedAtUtc, User = null! });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // RunningTimers.UserId is the primary key, so a failed insert here means another
            // request already started a timer for this user concurrently.
            return TimerResult.Fail("TIMER_ALREADY_RUNNING", "計時器已經在執行中。");
        }

        return TimerResult.Ok(new TimerResponse(true, startedAtUtc));
    }

    public async Task<StopTimerResult> StopAsync(ulong userId, string? name, ulong? categoryId, List<string>? tagNames, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var runningTimer = await dbContext.RunningTimers
            .SingleOrDefaultAsync(timer => timer.UserId == userId, cancellationToken);

        if (runningTimer is null)
        {
            return StopTimerResult.Fail("TIMER_NOT_RUNNING", "目前沒有正在執行的計時器。");
        }

        Category? category = null;
        if (categoryId is not null)
        {
            category = await entryService.GetOwnedCategoryAsync(userId, categoryId.Value, cancellationToken);
            if (category is null)
            {
                return StopTimerResult.Fail("CATEGORY_NOT_FOUND", "找不到指定的分類。");
            }
        }

        var normalizedTagNames = TimeEntryService.NormalizeTagNames(tagNames);
        var tags = await entryService.FindOrCreateTagsAsync(userId, normalizedTagNames, cancellationToken);

        var endTimeUtc = clock.UtcNow;
        if (endTimeUtc <= runningTimer.StartedAtUtc)
        {
            // Guards the EndTimeUtc > StartTimeUtc check constraint if Stop is called in the same
            // clock tick as Start (fast automated calls, low-resolution system clocks, etc.).
            endTimeUtc = runningTimer.StartedAtUtc.AddTicks(1);
        }

        var timeEntry = new TimeEntry
        {
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            StartTimeUtc = runningTimer.StartedAtUtc,
            EndTimeUtc = endTimeUtc,
            CategoryId = category?.Id,
            CreatedAtUtc = endTimeUtc,
            UpdatedAtUtc = endTimeUtc,
            User = null!,
        };
        foreach (var tag in tags)
        {
            timeEntry.TimeEntryTags.Add(new TimeEntryTag { TimeEntry = timeEntry, Tag = tag });
        }

        dbContext.TimeEntries.Add(timeEntry);
        dbContext.RunningTimers.Remove(runningTimer);

        try
        {
            // The DELETE below takes a row lock on RunningTimers for the duration of this
            // transaction. If another device's Stop request already deleted the same row inside
            // a still-uncommitted transaction, this DELETE blocks, then affects zero rows once
            // that transaction commits. EF Core surfaces a zero-row DELETE as
            // DbUpdateConcurrencyException, which we treat as "nothing left to stop" and roll
            // back -- this is what keeps concurrent Stop calls from creating more than one
            // TimeEntry, without needing a database-specific locking hint.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StopTimerResult.Fail("TIMER_NOT_RUNNING", "目前沒有正在執行的計時器。");
        }

        await transaction.CommitAsync(cancellationToken);

        return StopTimerResult.Ok(new TimeEntryResponse(
            timeEntry.Id,
            timeEntry.Name,
            timeEntry.StartTimeUtc,
            timeEntry.EndTimeUtc,
            timeEntry.CategoryId,
            CategoryName: category?.Name,
            CategoryColor: category?.Color,
            Tags: tags.Select(t => t.Name).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
            (long)(timeEntry.EndTimeUtc - timeEntry.StartTimeUtc).TotalSeconds));
    }
}
