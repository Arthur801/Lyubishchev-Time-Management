# TimeAggregationService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 以使用者時區和半開區間產生可供 Dashboard、Report 重用的總時長、每日、Category 與 Tag 統計。

**Architecture:** `TimeAggregationService` 讀取 `TimeZoneCatalog`、`IClock` 與相交 TimeEntries；EF 只做 ownership/range filter，交集、當地日期分桶、DST 轉換在記憶體完成。此服務是 read-only，Dashboard/Report API 不在本計畫範圍。

**Tech Stack:** .NET 10、EF Core/SQLite tests、IClock、TimeZoneInfo、xUnit。

---

## 檔案與責任

| 檔案 | 變更 |
| --- | --- |
| `Models/Responses/TimeAggregationResult.cs` | `LocalDateRange`、preset、daily/category/tag DTO。 |
| `Services/TimeAggregationService.cs` | range、UTC boundary、intersection、aggregation。 |
| `Tests/TimeEntryFlow.Tests/Integration/TimeAggregationServiceTests.cs` | SQLite integration and DST tests。 |

### Task 1: 定義 DTO 與 UTC 範圍

- [ ] **Step 1: 先寫範圍失敗測試**

```csharp
[Fact]
public void ThisWeek_in_AsiaTaipei_starts_on_sunday_and_ends_exclusively_next_sunday()
{
    var range = service.GetRangeForPreset(DateRangePreset.ThisWeek, "Asia/Taipei");
    Assert.Equal(new DateOnly(2026, 9, 13), range.StartDate);
    Assert.Equal(new DateOnly(2026, 9, 19), range.EndDateInclusive);
}
```

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~TimeAggregationServiceTests.ThisWeek"`

Expected: FAIL because DTO and service are absent.

- [ ] **Step 2: 定義 API 與半開範圍**

```csharp
public enum DateRangePreset { Today, ThisWeek, ThisMonth }
public sealed record LocalDateRange(DateOnly StartDate, DateOnly EndDateInclusive);
public sealed record DailyTotal(DateOnly Date, long DurationSeconds);
public sealed record CategoryTotal(ulong? CategoryId, string Name, string Color, long DurationSeconds);
public sealed record TagTotal(ulong TagId, string Name, long DurationSeconds);
public sealed record TimeAggregationResult(DateTime RangeStartUtc, DateTime RangeEndUtc, string TimeZoneId, long TotalSeconds, IReadOnlyList<DailyTotal> DailyTotals, IReadOnlyList<CategoryTotal> CategoryTotals, IReadOnlyList<TagTotal> TagTotals);
```

Use `clock.UtcNow` converted through `catalog.ResolveOrUtc(id)` to get the local Today; Sunday is `localDate.AddDays(-(int)localDate.DayOfWeek)`. Convert `StartDate 00:00` and `(EndDateInclusive + 1) 00:00` to UTC, reject reversed range with `ArgumentException`.

- [ ] **Step 3: Verify focused tests pass**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~TimeAggregationServiceTests"`

Expected: range tests PASS.

### Task 2: Implement aggregation behavior with tests

- [ ] **Step 1: add failing behavior tests**

```csharp
[Fact]
public async Task AggregateAsync_splits_cross_midnight_entry_into_local_daily_totals()
{
    // Asia/Taipei: 2026-01-01 23:00 to 2026-01-02 01:00 local.
    var result = await service.AggregateAsync(userId, new(new(2026,1,1), new(2026,1,2)), "Asia/Taipei", CancellationToken.None);
    Assert.Equal(7200, result.TotalSeconds);
    Assert.Equal([3600L, 3600L], result.DailyTotals.Select(x => x.DurationSeconds));
}
```

Add separate tests for zero-entry dates, boundary exclusion, Uncategorized `#a6adb7`, Category total equals overall total, Tag additive total can exceed overall total, overlapping entries remain additive, cross-user isolation, and New York spring/fall DST ranges.

- [ ] **Step 2: implement query and aggregation**

```csharp
var entries = await dbContext.TimeEntries.AsNoTracking()
    .Where(e => e.UserId == userId && e.StartTimeUtc < rangeEndUtc && e.EndTimeUtc > rangeStartUtc)
    .Include(e => e.Category).Include(e => e.TimeEntryTags).ThenInclude(x => x.Tag)
    .ToListAsync(cancellationToken);
var start = entry.StartTimeUtc > rangeStartUtc ? entry.StartTimeUtc : rangeStartUtc;
var end = entry.EndTimeUtc < rangeEndUtc ? entry.EndTimeUtc : rangeEndUtc;
var seconds = (long)(end - start).TotalSeconds;
```

Skip entries where `end <= start`. For daily totals, advance local date by date and intersect the entry with each local day's converted UTC boundary. Prepopulate every date in `LocalDateRange` with zero. Group Category/Tag from each entry's whole in-range duration; deterministic sort is duration descending then name ordinal-ignore-case.

- [ ] **Step 3: run regression and document consumers**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore`

Expected: PASS. Record that Dashboard, Report, History filter migration, and CSV export consume this service in later feature tasks; do not add their controllers or JavaScript here.

## Plan self-review

- Timezone plan changes only `User.TimeZoneId` and uses the existing `IClock`, JWT/CSRF and Problem Details conventions.
- Aggregation plan uses the existing `TimeEntry` UTC storage and overlap query; it preserves the documented rule that overlapping durations and Tag totals are additive.
- Neither plan assumes Category/Tag management has been implemented, so it remains valid against the current project state.
