# Timezone Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 使用者可安全地從常用 IANA 時區清單選擇時區，且不改寫任何既有 UTC 時間。

**Architecture:** `TimeZoneCatalog` 是唯一的時區白名單與解析入口；`UserSettingsService` 以目前使用者為範圍更新 `User.TimeZoneId`；Settings API 與頁面只消費 service response。這份工作不改 Dashboard、Report、History 日期篩選或 TimeEntry 的 UTC 欄位。

**Tech Stack:** ASP.NET Core MVC、EF Core、IClock、Razor、原生 JavaScript、xUnit。

---

## 檔案與責任

| 檔案 | 變更 |
| --- | --- |
| `Infrastructure/Time/TimeZoneCatalog.cs` | 白名單、顯示名稱、解析與 UTC fallback。 |
| `Models/Requests/UpdateTimezoneRequest.cs` | `TimeZoneId` request 驗證。 |
| `Models/Responses/TimezoneSettingsResponse.cs` | 目前值與選項 DTO。 |
| `Services/UserSettingsService.cs` | 讀取、驗證及更新使用者設定。 |
| `Controllers/SettingsController.cs`、`Controllers/Api/SettingsApiController.cs` | 受保護頁面與 GET/PATCH API。 |
| `Views/Settings/Index.cshtml`、`wwwroot/js/settings.js`、`wwwroot/css/settings.css` | 設定 UI 與 CSRF fetch。 |
| `Program.cs` | 註冊 catalog 與 service。 |
| `Tests/TimeEntryFlow.Tests/Integration/UserSettingsServiceTests.cs` | SQLite service tests。 |

### Task 1: 時區目錄與 service 測試

- [ ] **Step 1: 先新增失敗測試**

```csharp
[Fact]
public async Task UpdateAsync_updates_only_the_current_users_timezone_and_timestamp()
{
    var result = await service.UpdateAsync(userId, "America/New_York", CancellationToken.None);
    Assert.True(result.Succeeded);
    Assert.Equal("America/New_York", user.TimeZoneId);
    Assert.Equal(clock.UtcNow, user.UpdatedAtUtc);
    Assert.Equal(originalEntryStartUtc, entry.StartTimeUtc);
}
```

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~UserSettingsServiceTests"`

Expected: FAIL because catalog and service methods do not exist.

- [ ] **Step 2: 實作白名單與結果模型**

```csharp
public sealed record TimeZoneOption(string Id, string DisplayName);
public sealed class TimeZoneCatalog
{
    public static readonly IReadOnlyList<TimeZoneOption> Options = [
        new("UTC", "協調世界時間 (UTC)"), new("Asia/Taipei", "台北 (UTC+08:00)"),
        new("Asia/Tokyo", "東京 (UTC+09:00)"), new("Asia/Singapore", "新加坡 (UTC+08:00)"),
        new("Asia/Hong_Kong", "香港 (UTC+08:00)"), new("Europe/London", "倫敦"), new("Europe/Paris", "巴黎"),
        new("America/New_York", "紐約"), new("America/Chicago", "芝加哥"), new("America/Denver", "丹佛"),
        new("America/Los_Angeles", "洛杉磯"), new("Australia/Sydney", "雪梨"), new("Pacific/Auckland", "奧克蘭")];
    public bool IsSupported(string? id) => Options.Any(x => x.Id == id);
    public TimeZoneInfo ResolveOrUtc(string? id) => IsSupported(id) ? TimeZoneInfo.FindSystemTimeZoneById(id!) : TimeZoneInfo.Utc;
}
```

`UpdateAsync` must reject blank/unsupported input with `INVALID_TIME_ZONE`, query `Users.SingleOrDefaultAsync(u => u.Id == userId)`, set only `TimeZoneId` and `UpdatedAtUtc`, then save. Add tests for invalid ID, missing user, repeated selection, and another user's record remaining unchanged.

- [ ] **Step 3: 驗證 service**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~UserSettingsServiceTests"`

Expected: PASS.

### Task 2: API、頁面與驗收

- [ ] **Step 1: 實作 server boundary**

```csharp
[Authorize, Route("api/settings")]
public sealed class SettingsApiController(UserSettingsService service, CurrentUserService currentUser) : ControllerBase
{
    [HttpGet("timezone")] public async Task<IActionResult> Get(CancellationToken ct) => Ok(await service.GetAsync(currentUser.GetRequiredUserId(), ct));
    [HttpPatch("timezone"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromBody] UpdateTimezoneRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.UpdateAsync(currentUser.GetRequiredUserId(), request.TimeZoneId, ct);
        return result.Succeeded ? Ok(result.Settings) : result.ErrorCode == "INVALID_TIME_ZONE"
            ? Problem(result.ErrorMessage, statusCode: 400, title: result.ErrorCode)
            : Problem(result.ErrorMessage, statusCode: 404, title: result.ErrorCode);
    }
}
```

Register `TimeZoneCatalog` singleton and `UserSettingsService` scoped. `SettingsController.Index` is `[Authorize]` and returns its View without querying DbContext.

- [ ] **Step 2: 實作 Razor 與 JavaScript**

The page has `#timezone-select`, `#timezone-save`, `#timezone-message`; on load, GET `/api/settings/timezone`, populate options from response, select `timeZoneId`; on submit PATCH `{ timeZoneId: select.value }` with `X-CSRF-TOKEN`. Success and Problem Details `detail` are written through `textContent`. No browser-detected value is submitted automatically.

- [ ] **Step 3: 最終檢查**

Run: `dotnet build ".\Lyubishchev Time Management.csproj" --no-restore`

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore`

Expected: both succeed; manually verify `/Settings` persists a supported choice and an invalid PATCH returns 400 `INVALID_TIME_ZONE`.
