# Calendar View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在既有 `/TimeEntry` History 頁新增唯讀的 List／Calendar 切換，桌面顯示週時間軸、手機顯示單日行事曆。

**Architecture:** 後端維持 `TimeEntryService.ListAsync` 的 user-owned overlap query 與 `TimeEntryResponse`；前端 `calendar.js` 只處理可視範圍、帳號時區轉換和畫面切段。Calendar 依賴 Timezone Settings 的帳號時區來源，不另建資料模型或 CRUD API。

**Tech Stack:** ASP.NET Core MVC、EF Core、Razor、原生 JavaScript、CSS、SQLite xUnit tests。

---

## 檔案清單

| 檔案 | 工作 |
| --- | --- |
| `Models/ViewModels/HistoryViewModel.cs` | 加入時區設定或 settings endpoint 所需的最小初始資料。 |
| `Controllers/TimeEntryController.cs` | 保持同一頁面入口並組裝 ViewModel。 |
| `Controllers/Api/TimeEntryApiController.cs`、`Services/TimeEntryService.cs` | 僅允許有效可視 UTC range 的 overlap 查詢。 |
| `Views/TimeEntry/Index.cshtml` | List／Calendar tabs、Calendar navigation 與容器。 |
| `wwwroot/js/calendar.js` | week/day 範圍、fetch、切段與 rendering。 |
| `wwwroot/css/calendar.css` | week grid、day layout、重疊與 RWD。 |
| `Tests/TimeEntryFlow.Tests/Integration/TimeEntryServiceTests.cs` | 可視範圍與跨使用者回歸測試。 |

### Task 1: 保護可視範圍資料讀取

- [ ] **Step 1: 寫失敗測試**

```csharp
[Fact]
public async Task ListAsync_returns_only_entries_intersecting_the_visible_range_for_the_current_user()
{
    var result = await service.ListAsync(userId, startUtc, endUtc, null, null, 1, 200, CancellationToken.None);
    Assert.Equal(new[] { intersectingEntryId }, result.Items.Select(x => x.Id));
}
```

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~TimeEntryServiceTests.ListAsync_returns_only_entries_intersecting"`

Expected: fail until the dedicated visible-range case exists; it must prove a fully outside entry and another user's intersecting entry are excluded.

- [ ] **Step 2: implement only the server validation needed by Calendar**

`TimeEntryApiController.List` rejects `endUtc <= startUtc` with existing `INVALID_TIME_RANGE` 400. `TimeEntryService.ListAsync` keeps the existing predicates `EndTimeUtc > startUtc` and `StartTimeUtc < endUtc`, user filter, `AsNoTracking`, and 200 maximum page size; do not create a second Calendar data service or return all records.

- [ ] **Step 3: verify regression**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore`

Expected: PASS.

### Task 2: 建立 History 內的 Calendar shell

- [ ] **Step 1: add Razor structure**

Add two buttons with `data-history-view="list"` and `data-history-view="calendar"`, `aria-pressed`, a calendar navigation group (`#calendar-previous`, `#calendar-today`, `#calendar-next`), `#calendar-range-label`, and `#calendar-grid`. Keep the current list filters, modal, edit, delete and pagination markup unchanged; hide inactive view with `hidden`.

- [ ] **Step 2: add style boundaries**

Create `calendar.css` loaded only by `Views/TimeEntry/Index.cshtml`. Desktop has a seven-column Sunday-first grid with hour rows; mobile media query hides the weekly grid and renders one day column. Use CSS custom properties already defined by `dashboard.css`; Category color is an inline CSS variable and uncategorized is `#a6adb7`.

- [ ] **Step 3: manual structural check**

Run: `dotnet run --project ".\Lyubishchev Time Management.csproj"`

Expected: `/TimeEntry` still renders existing List view by default; keyboard focus can select Calendar and navigation controls at 320px and desktop width.

### Task 3: 實作可視週／日 Calendar rendering

- [ ] **Step 1: write front-end behavior cases before rendering**

Document in `calendar.js` tests or a browser test harness: Sunday anchor produces seven desktop dates; mobile produces one date; an entry spanning local 23:00–01:00 creates two display blocks; an entry with no category receives `#a6adb7`; overlapping entries both remain visible.

- [ ] **Step 2: implement state and range flow**

`calendar.js` state is `{ mode: "week" | "day", anchorDate, entries, timeZoneId }`. It obtains `timeZoneId` from the completed Settings API, converts local start/end to UTC, calls `GET /api/time-entries?startUtc=...&endUtc=...&page=1&pageSize=200`, and renders only the response items. Desktop navigation adds/subtracts seven days; mobile adds/subtracts one day; Today resets anchor through the account timezone.

- [ ] **Step 3: implement display-only splitting**

For each local visible date, derive that date's UTC bounds and render `max(entryStart, dayStart)` to `min(entryEnd, dayEnd)` only if positive. Use `textContent` for Name and Tags, set `--entry-color` from `categoryColor ?? "#a6adb7"`, and do not issue POST/PATCH/DELETE from Calendar.

- [ ] **Step 4: final verification**

Run: `dotnet build ".\Lyubishchev Time Management.csproj" --no-restore`

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore`

Expected: both pass. Manually verify List remains functional after switching back from Calendar, and test a cross-midnight, overlapping, uncategorized, tagged record at desktop and 320px widths.

## Plan consistency check

- Uses the existing `/api/time-entries` ownership and overlap rules rather than duplicating TimeEntry access.
- Depends on the documented Timezone Settings feature; it does not make browser timezone a persisted data rule.
- Excludes Calendar editing, drag/drop and resize as required by `AGENTS.md` V1 scope.
