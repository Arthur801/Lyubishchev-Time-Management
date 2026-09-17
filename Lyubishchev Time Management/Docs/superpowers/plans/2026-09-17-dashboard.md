# Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將 Dashboard mock data 替換成基於帳號時區與 TimeAggregationService 的真實統計。

**Architecture:** DashboardService 取得本期、比較期 aggregation 與最近五筆 owned entries，Dashboard API 回傳單一 DTO，dashboard.js 只渲染該 DTO。Timer 繼續獨立於 timer.js。

**Tech Stack:** ASP.NET Core MVC、EF Core、TimeAggregationService、Razor、原生 JavaScript、xUnit SQLite tests。

---

## 檔案

| 檔案 | 變更 |
| --- | --- |
| `Models/Requests/DashboardRequest.cs`、`Models/Responses/DashboardResponse.cs` | range request 與 API DTO。 |
| `Services/DashboardService.cs` | 本期／比較期、recent entries 組裝。 |
| `Controllers/Api/DashboardApiController.cs`、`Program.cs` | 授權 GET endpoint 與 DI。 |
| `wwwroot/js/dashboard.js`、`wwwroot/js/dashboard-state.mjs` | 真實 fetch、移除 mock。 |
| `Views/Dashboard/Index.cshtml` | 空狀態與資料屬性最小調整。 |
| `Tests/TimeEntryFlow.Tests/Integration/DashboardServiceTests.cs` | aggregation consumer tests。 |

### Task 1: Dashboard service 與 DTO

- [ ] **Step 1: 寫失敗測試**

```csharp
[Fact]
public async Task GetAsync_uses_the_previous_adjacent_range_for_comparison()
{
    var response = await service.GetAsync(userId, DashboardRange.Today, null, null, CancellationToken.None);
    Assert.Equal(todaySeconds, response.TotalSeconds);
    Assert.Equal(yesterdaySeconds, response.ComparisonSeconds);
    Assert.Equal(todaySeconds - yesterdaySeconds, response.ChangeSeconds);
}
```

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~DashboardServiceTests"`

Expected: FAIL because DashboardService and DTOs are empty.

- [ ] **Step 2: implement the minimal service boundary**

`DashboardRequest` has nullable `Preset`, `StartDate`, `EndDate`; exactly one valid mode is required. `DashboardService.GetAsync` loads the owned User settings, obtains `LocalDateRange` through TimeAggregationService, derives comparison range, calls aggregation twice, and queries `TimeEntries.Where(e => e.UserId == userId).OrderByDescending(e => e.EndTimeUtc).ThenByDescending(e => (long)e.Id).Take(5)` with Category and Tags. Return totals and no user entities.

- [ ] **Step 3: add behavior tests and verify**

Add tests for week/month/custom range, invalid dates, empty data, recent five ordering, cross-user exclusion, Uncategorized and Tag additive totals. Run the focused command again; expected PASS.

### Task 2: API and dependency injection

- [ ] **Step 1: add GET endpoint**

```csharp
[Authorize, Route("api/dashboard")]
public sealed class DashboardApiController(DashboardService service, CurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DashboardRequest request, CancellationToken ct)
    {
        var result = await service.GetAsync(currentUser.GetRequiredUserId(), request.Preset, request.StartDate, request.EndDate, ct);
        return result.Succeeded ? Ok(result.Response) : Problem(result.ErrorMessage, statusCode: 400, title: result.ErrorCode);
    }
}
```

Register DashboardService scoped. Map invalid date/preset to the documented 400 titles; unexpected failures are not translated into fake empty data.

- [ ] **Step 2: build and test**

Run: `dotnet build ".\Lyubishchev Time Management.csproj" --no-restore`

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore`

Expected: both PASS.

### Task 3: Replace front-end mocks

- [ ] **Step 1: fetch and render response**

`dashboard.js` sends selected preset or both custom dates to `/api/dashboard`; it uses the existing CSRF helper only for state-changing requests, renders `detail` into `#range-error`, and guards against stale fetch responses with an incrementing request id. It uses `textContent` for names and empty-state text.

- [ ] **Step 2: handle chart edge cases**

When total is zero, render `0 小時 0 分`, an empty Category message and no conic gradient. When totals exist, use Category colors from response; render Tags relative to the largest Tag duration, never relative to total duration. If no recent entries exist, render `尚無完成紀錄`.

- [ ] **Step 3: remove mock dependency and verify manually**

Remove `dashboard-state.mjs` imports and unused mock-only exports/files. Test Today, Week, Month, custom range, API error, empty account, recent link to `/TimeEntry`, desktop and 320px layouts; start/stop a timer to confirm timer.js remains independent.

Run: `dotnet test .\Tests\AuthFlow.Tests\AuthFlow.Tests.csproj --no-restore; dotnet test .\Tests\TimerFlow.Tests\TimerFlow.Tests.csproj --no-restore; dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore`

Expected: all PASS.

## Consistency check

- Dashboard consumes, but does not duplicate, TimeAggregationService's account-timezone, half-open interval, daily, Category and Tag rules.
- Comparison is always an adjacent equal-length local range and only affects the summary delta.
- Timer, Calendar, Report and CSV are excluded from this implementation plan.
