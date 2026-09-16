# Category 與 Tag 管理 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓已登入使用者能在獨立頁面管理 Category 與 Tag，並以 Trim + 忽略大小寫規則確保同一使用者名稱唯一。

**Architecture:** 新增持久化的 `NormalizedName` 作為不分大小寫的唯一鍵；保留 `Name` 作為顯示值。`CategoryService` 和 `TagService` 集中處理擁有權、正規化、衝突與時間戳，API Controller 只負責 request 驗證、目前使用者與 HTTP mapping。Razor/ES module 管理頁透過相同的 JSON API 更新自己的列表。

**Tech Stack:** ASP.NET Core MVC、.NET 10、EF Core/MySQL、SQLite in-memory xUnit integration tests、Razor、原生 JavaScript、CSS。

---

## 已確認的資料與 API 契約

- 顯示名稱：`Name = rawName.Trim()`；唯一鍵：`NormalizedName = Name.ToUpperInvariant()`。
- `Categories(UserId, NormalizedName)`、`Tags(UserId, NormalizedName)` 都是 unique index。此欄位避免依賴 MySQL/SQLite 預設 collation，讓 `Focus` 與 ` focus ` 在兩個 provider 都必定衝突。
- `CategoryRequest`：`Name`（必填、100 字內）與 `Color`（必填、`#RRGGBB`）；`TagRequest`：`Name`（必填、100 字內）。全空白名稱由 Service 回 `INVALID_NAME` 400。
- 成功：GET 200、POST 201、PATCH 200、DELETE 204。錯誤：同名為 409 `CATEGORY_NAME_CONFLICT`/`TAG_NAME_CONFLICT`；跨使用者或不存在為 404 `CATEGORY_NOT_FOUND`/`TAG_NOT_FOUND`。
- Category delete 維持既有 `SET NULL`；Tag delete 維持既有 `CASCADE` 至 `TimeEntryTags`。不刪 TimeEntry。

## 預計變更檔案

| 檔案 | 責任 |
| --- | --- |
| `Models/Entities/Category.cs`, `Models/Entities/Tag.cs` | 加入持久化的 `NormalizedName`。 |
| `Infrastructure/Text/ResourceName.cs` | 唯一的名稱 Trim、長度與不分大小寫 key 規則。 |
| `Data/Configurations/CategoryConfiguration.cs`, `Data/Configurations/TagConfiguration.cs` | 宣告 `NormalizedName` 欄位與 unique composite indexes。 |
| `Data/Migrations/*_AddNormalizedResourceNames.cs`, `AppDbContextModelSnapshot.cs` | 回填既有資料、新增不可為 null 欄位與 index 的 MySQL migration。 |
| `Models/Requests/CategoryRequest.cs`, `TagRequest.cs` | JSON request 驗證。 |
| `Models/Responses/CategoryResponse.cs`, `TagResponse.cs` | 不含 UserId 的 API DTO。 |
| `Services/CategoryService.cs`, `TagService.cs` | CRUD、result records、ownership 與 conflict handling。 |
| `Services/TimeEntryService.cs` | 讓 inline Tag 使用 `ResourceName` 與 `NormalizedName` 查找。 |
| `Program.cs` | 註冊兩個 scoped service。 |
| `Controllers/CategoryController.cs`, `TagController.cs` | 受保護的管理頁入口。 |
| `Controllers/Api/CategoryApiController.cs`, `TagApiController.cs` | 授權、CSRF、request 與 HTTP mapping。 |
| `Views/Category/Index.cshtml`, `Views/Tag/Index.cshtml` | 管理頁語意骨架。 |
| `Views/Dashboard/Index.cshtml`, `Views/TimeEntry/Index.cshtml` | 將 Category/Tag 側欄連到真實路由。 |
| `wwwroot/js/category.js`, `wwwroot/js/tag.js`, `wwwroot/css/category-tag.css` | API 呼叫、表單、確認刪除及 RWD UI。 |
| `Tests/TimeEntryFlow.Tests/TestDatabase.cs`, `Integration/CategoryServiceTests.cs`, `Integration/TagServiceTests.cs` | 共用 SQLite fixture 與整合測試。 |

### Task 1: 建立名稱唯一鍵與資料庫 migration

**Files:**
- Create: `Lyubishchev Time Management/Infrastructure/Text/ResourceName.cs`
- Modify: `Lyubishchev Time Management/Models/Entities/Category.cs`
- Modify: `Lyubishchev Time Management/Models/Entities/Tag.cs`
- Modify: `Lyubishchev Time Management/Data/Configurations/CategoryConfiguration.cs`
- Modify: `Lyubishchev Time Management/Data/Configurations/TagConfiguration.cs`
- Create: `Lyubishchev Time Management/Data/Migrations/*_AddNormalizedResourceNames.cs` (由下列 EF 命令產生)
- Modify: `Lyubishchev Time Management/Data/Migrations/AppDbContextModelSnapshot.cs`
- Create: `Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/TestDatabase.cs`
- Test: `Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration/CategoryServiceTests.cs`

- [ ] **Step 1: 寫會失敗的 SQLite 約束測試**

```csharp
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
```

- [ ] **Step 2: 驗證測試確實失敗**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~CategoryServiceTests.Category_unique_key_rejects_trimmed_case_insensitive_duplicates"`

Expected: FAIL，因為目前沒有 `NormalizedName` 欄位與 `(UserId, NormalizedName)` unique index。

- [ ] **Step 3: 實作名稱鍵與 EF Core mapping**

```csharp
// Infrastructure/Text/ResourceName.cs
namespace Lyubishchev_Time_Management.Infrastructure.Text;

public static class ResourceName
{
    public static bool TryNormalize(string? rawName, out string name, out string normalizedName)
    {
        name = rawName?.Trim() ?? string.Empty;
        normalizedName = name.ToUpperInvariant();
        return name.Length is > 0 and <= 100;
    }
}

// Tests/TimeEntryFlow.Tests/TestDatabase.cs
internal static class TestDatabase
{
    public static async Task<(SqliteConnection KeepAlive, DbContextOptions<AppDbContext> Options, ulong UserId)> CreateAsync()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (keepAlive, options, await CreateUserAsync(db));
    }

    public static async Task<ulong> CreateUserAsync(AppDbContext db)
    {
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "not-a-real-hash", TimeZoneId = "Asia/Taipei", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
    public static Category Category(ulong userId, string name, string normalizedName, string color) => new()
    { UserId = userId, Name = name, NormalizedName = normalizedName, Color = color, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, User = null! };
    public static async Task<Category> CreateCategoryAsync(AppDbContext db, ulong userId, string name) { var category = Category(userId, name, name.Trim().ToUpperInvariant(), "#e5533d"); db.Categories.Add(category); await db.SaveChangesAsync(); return category; }
}

// Add to both Category and Tag entities.
public required string NormalizedName { get; set; }

// CategoryConfiguration and TagConfiguration each configure this member.
builder.Property(resource => resource.NormalizedName).HasMaxLength(100).IsRequired();
builder.HasIndex(resource => new { resource.UserId, resource.NormalizedName }).IsUnique();
```

Replace the old `Category(UserId, Name)` and `Tag(UserId, Name)` indexes rather than retaining redundant uniqueness checks. In the generated migration, add `NormalizedName` nullable, run `UPDATE ... SET NormalizedName = UPPER(TRIM(Name))`, then alter it to non-null and create the unique indexes. The `Down` method drops each new unique index and column, then restores the old non-unique Category index and unique Tag `(UserId, Name)` index.

- [ ] **Step 4: Generate, inspect, and apply the migration locally**

Run: `dotnet ef migrations add AddNormalizedResourceNames --project ".\Lyubishchev Time Management.csproj" --startup-project ".\Lyubishchev Time Management.csproj"`

Then edit the generated `Up` so its data migration is explicit:

```csharp
migrationBuilder.Sql("UPDATE `Categories` SET `NormalizedName` = UPPER(TRIM(`Name`));");
migrationBuilder.Sql("UPDATE `Tags` SET `NormalizedName` = UPPER(TRIM(`Name`));");
```

Run: `dotnet ef database update --project ".\Lyubishchev Time Management.csproj" --startup-project ".\Lyubishchev Time Management.csproj"`

Expected: database is current. If a pre-existing database already contains duplicate normalized names, the unique-index creation intentionally fails; resolve or rename those records explicitly before rerunning so the migration never silently loses data.

- [ ] **Step 5: Rerun the focused test**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~CategoryServiceTests.Category_unique_key_rejects_trimmed_case_insensitive_duplicates"`

Expected: PASS.

- [ ] **Step 6: Commit schema work**

```bash
git add "Lyubishchev Time Management/Infrastructure/Text/ResourceName.cs" "Lyubishchev Time Management/Models/Entities/Category.cs" "Lyubishchev Time Management/Models/Entities/Tag.cs" "Lyubishchev Time Management/Data" "Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration/CategoryServiceTests.cs"
git commit -m "feat: enforce normalized category and tag names"
```

### Task 2: 實作 Category request、response、service 與測試

**Files:**
- Modify: `Lyubishchev Time Management/Models/Requests/CategoryRequest.cs`
- Create: `Lyubishchev Time Management/Models/Responses/CategoryResponse.cs`
- Modify: `Lyubishchev Time Management/Services/CategoryService.cs`
- Create: `Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration/CategoryServiceTests.cs`

- [ ] **Step 1: 寫 Category service 的失敗測試**

```csharp
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
```

- [ ] **Step 2: Run focused tests to verify they fail**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~CategoryServiceTests"`

Expected: FAIL because `CategoryRequest`, `CategoryResponse`, and `CategoryService` CRUD members do not exist.

- [ ] **Step 3: Add contract and service implementation**

```csharp
// Models/Requests/CategoryRequest.cs
public sealed class CategoryRequest
{
    [Required, StringLength(100)] public string? Name { get; set; }
    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")] public string? Color { get; set; }
}

// Models/Responses/CategoryResponse.cs
public sealed record CategoryResponse(ulong Id, string Name, string Color);

// Services/CategoryService.cs: public operation shape
public sealed record CategoryResult(bool Succeeded, CategoryResponse? Category, string? ErrorCode, string? ErrorMessage);
public sealed record CategoryDeleteResult(bool Succeeded, string? ErrorCode, string? ErrorMessage);

public async Task<IReadOnlyList<CategoryResponse>> ListAsync(ulong userId, CancellationToken ct) =>
    await dbContext.Categories.AsNoTracking().Where(c => c.UserId == userId)
        .OrderBy(c => c.NormalizedName)
        .Select(c => new CategoryResponse(c.Id, c.Name, c.Color)).ToListAsync(ct);
```

For Create/Update, call `ResourceName.TryNormalize`; return `INVALID_NAME` on false, query by `UserId` plus `NormalizedName`, and return `CATEGORY_NAME_CONFLICT` when another row occupies the key. Create with both display and normalized values plus `clock.UtcNow`; Update only the owned entity and set `UpdatedAtUtc`. Catch `DbUpdateException` around save and re-query the conflicting key, returning conflict only if it now exists; otherwise rethrow. Delete with `ExecuteDeleteAsync` filtered by both id and user id.

- [ ] **Step 4: Add behavior coverage before considering the task complete**

Add focused facts for ordered List, invalid whitespace-only name (`INVALID_NAME`), own update preserving `CreatedAtUtc`, own delete, same name for different users, and deleting a Category assigned to a TimeEntry leaves the entry with `CategoryId == null`.

- [ ] **Step 5: Run Category tests**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~CategoryServiceTests"`

Expected: PASS.

- [ ] **Step 6: Commit Category domain behavior**

```bash
git add "Lyubishchev Time Management/Models/Requests/CategoryRequest.cs" "Lyubishchev Time Management/Models/Responses/CategoryResponse.cs" "Lyubishchev Time Management/Services/CategoryService.cs" "Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration/CategoryServiceTests.cs"
git commit -m "feat: add category management service"
```

### Task 3: 實作 Tag service，並使 inline Tag 使用同一規則

**Files:**
- Modify: `Lyubishchev Time Management/Models/Requests/TagRequest.cs`
- Create: `Lyubishchev Time Management/Models/Responses/TagResponse.cs`
- Modify: `Lyubishchev Time Management/Services/TagService.cs`
- Modify: `Lyubishchev Time Management/Services/TimeEntryService.cs`
- Create: `Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration/TagServiceTests.cs`
- Modify: `Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration/TimeEntryServiceTests.cs`

- [ ] **Step 1: Add failing Tag and inline behavior tests**

```csharp
[Fact]
public async Task CreateAsync_rejects_case_insensitive_duplicate_but_allows_another_user()
{
    var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
    await using var _ = keepAlive;
    await using var db = new AppDbContext(options);
    var service = new TagService(db, new TestClock(DateTime.UtcNow));
    Assert.True((await service.CreateAsync(userId, new TagRequest { Name = "Focus" }, default)).Succeeded);
    Assert.Equal("TAG_NAME_CONFLICT", (await service.CreateAsync(userId, new TagRequest { Name = " focus ", }, default)).ErrorCode);
    var otherUserId = await TestDatabase.CreateUserAsync(db);
    Assert.True((await service.CreateAsync(otherUserId, new TagRequest { Name = "focus" }, default)).Succeeded);
}

[Fact]
public async Task CreateAsync_inline_tag_reuses_the_normalized_key()
{
    // Create TimeEntry once with ["Focus"], then again with [" focus "].
    // Assert a single Tags row with Name "Focus" and two TimeEntryTags rows.
}
```

- [ ] **Step 2: Run the focused tests to confirm failure**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --filter "FullyQualifiedName~TagServiceTests|FullyQualifiedName~TimeEntryServiceTests.CreateAsync_inline_tag_reuses_the_normalized_key"`

Expected: FAIL because Tag contracts/service are absent and TimeEntry currently queries raw names.

- [ ] **Step 3: Add Tag CRUD and update `FindOrCreateTagsAsync`**

```csharp
// Models/Requests/TagRequest.cs
public sealed class TagRequest { [Required, StringLength(100)] public string? Name { get; set; } }

// Models/Responses/TagResponse.cs
public sealed record TagResponse(ulong Id, string Name);

// In TimeEntryService.NormalizeTagNames, normalize before de-duplication.
return rawNames.Select(raw => ResourceName.TryNormalize(raw, out var name, out var key) ? (name, key) : default)
    .Where(item => !string.IsNullOrEmpty(item.key))
    .DistinctBy(item => item.key, StringComparer.Ordinal)
    .Take(MaxTagsPerEntry).ToList();

// In FindOrCreateTagsAsync, query keys and create both values.
var existing = await dbContext.Tags.Where(t => t.UserId == userId && normalizedKeys.Contains(t.NormalizedName)).ToListAsync(cancellationToken);
var newTags = missing.Select(item => new Tag { UserId = userId, Name = item.name, NormalizedName = item.key, CreatedAtUtc = now, UpdatedAtUtc = now, User = null! }).ToList();
```

Adjust `FindOrCreateTagsAsync` and its callers to pass normalized `(Name, Key)` values and return display names. `TagService` mirrors CategoryService except it has no Color. Its delete method uses a direct owned Tag delete; the existing `TimeEntryTag` cascade handles link removal.

- [ ] **Step 4: Add the remaining Tag assertions**

Cover List order, whitespace invalid input, update conflict, cross-user update/delete rejection, concurrent duplicate Tag management creates (one success, one `TAG_NAME_CONFLICT`, exactly one row), and delete retaining TimeEntry while removing join rows.

- [ ] **Step 5: Run Tag and TimeEntry regression tests**

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj`

Expected: PASS for all TimeEntry, Category, and Tag tests.

- [ ] **Step 6: Commit Tag behavior**

```bash
git add "Lyubishchev Time Management/Models/Requests/TagRequest.cs" "Lyubishchev Time Management/Models/Responses/TagResponse.cs" "Lyubishchev Time Management/Services/TagService.cs" "Lyubishchev Time Management/Services/TimeEntryService.cs" "Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration"
git commit -m "feat: add tag management service"
```

### Task 4: 接上 DI、受保護的 MVC 頁面與 CRUD API

**Files:**
- Modify: `Lyubishchev Time Management/Program.cs`
- Modify: `Lyubishchev Time Management/Controllers/CategoryController.cs`
- Modify: `Lyubishchev Time Management/Controllers/TagController.cs`
- Modify: `Lyubishchev Time Management/Controllers/Api/CategoryApiController.cs`
- Modify: `Lyubishchev Time Management/Controllers/Api/TagApiController.cs`

- [ ] **Step 1: Preserve the established automated-test boundary**

The repository's current feature suites test application services directly (`AuthServiceTests`, `TimerServiceTests`, `TimeEntryServiceTests`) and do not include a `WebApplicationFactory` or a controller test host. Keep Category/Tag's automated assertions in Tasks 2–3 at that established service boundary. Do not change `CurrentUserService` or add test-only authentication plumbing merely to test thin controller forwarding.

- [ ] **Step 2: Implement DI and view controllers**

```csharp
// Program.cs
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<TagService>();

// Both MVC controllers use this complete shape.
[Authorize]
public sealed class CategoryController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
```

Use the same controller shape for Tag. Neither page controller queries `AppDbContext`; the browser obtains its list from API.

- [ ] **Step 3: Implement API controller routes and failure mapping**

```csharp
[Authorize]
[Route("api/categories")]
public sealed class CategoryApiController(CategoryService service, CurrentUserService currentUser) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(currentUser.GetRequiredUserId(), ct));
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CategoryRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.CreateAsync(currentUser.GetRequiredUserId(), request, ct);
        return result.Succeeded
            ? CreatedAtAction(nameof(List), new { id = result.Category!.Id }, result.Category)
            : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }
}
```

Add PATCH `{id}` and DELETE `{id}` with the same ownership call. `MapFailure` maps `INVALID_NAME` to 400, `CATEGORY_NAME_CONFLICT` to 409, and `CATEGORY_NOT_FOUND` to 404; unknown codes are 500. Copy the structure for Tag but substitute the Tag types and error codes.

- [ ] **Step 4: Build and run service regression suite**

Run: `dotnet build ".\Lyubishchev Time Management.csproj"`

Expected: `Build succeeded.`

Run: `dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit server boundary**

```bash
git add "Lyubishchev Time Management/Program.cs" "Lyubishchev Time Management/Controllers/CategoryController.cs" "Lyubishchev Time Management/Controllers/TagController.cs" "Lyubishchev Time Management/Controllers/Api/CategoryApiController.cs" "Lyubishchev Time Management/Controllers/Api/TagApiController.cs"
git commit -m "feat: expose category and tag CRUD APIs"
```

### Task 5: 建立 Category／Tag 管理頁、導覽與樣式

**Files:**
- Create: `Lyubishchev Time Management/Views/Category/Index.cshtml`
- Create: `Lyubishchev Time Management/Views/Tag/Index.cshtml`
- Modify: `Lyubishchev Time Management/Views/Dashboard/Index.cshtml`
- Modify: `Lyubishchev Time Management/Views/TimeEntry/Index.cshtml`
- Create: `Lyubishchev Time Management/wwwroot/css/category-tag.css`

- [ ] **Step 1: Add page skeletons before styling**

Use this shared Category body shape (Tag changes title, description, and removes color controls):

```cshtml
@{ ViewData["Title"] = "分類管理"; }
@section Styles { <link rel="stylesheet" href="~/css/dashboard.css" asp-append-version="true" /><link rel="stylesheet" href="~/css/category-tag.css" asp-append-version="true" /> }
<div class="dashboard-page" id="category-page">
  <aside class="app-sidebar" aria-label="主要導覽">
    <a class="app-brand" asp-controller="Dashboard" asp-action="Index" aria-label="LTM 儀表板"><span>LTM</span><small>TIME LOG</small></a>
    <nav class="app-nav">
      <a class="app-nav__link" asp-controller="Dashboard" asp-action="Index"><span aria-hidden="true">◈</span>儀表板</a>
      <a class="app-nav__link" asp-controller="TimeEntry" asp-action="Index"><span aria-hidden="true">◷</span>歷史紀錄</a>
      <a class="app-nav__link" href="#"><span aria-hidden="true">▥</span>報表</a>
      <a class="app-nav__link app-nav__link--active" asp-controller="Category" asp-action="Index" aria-current="page"><span aria-hidden="true">◌</span>分類</a>
      <a class="app-nav__link" asp-controller="Tag" asp-action="Index"><span aria-hidden="true">#</span>標籤</a>
      <a class="app-nav__link" href="#"><span aria-hidden="true">⚙</span>設定</a>
    </nav>
    <a class="app-nav__link app-nav__link--logout" href="#"><span aria-hidden="true">↗</span>登出</a>
  </aside>
  <main class="dashboard-content resource-content">
    <header class="page-heading"><div><p class="eyebrow">分類</p><h1>整理時間的脈絡。</h1><p id="resource-count">載入中…</p></div><button id="add-resource" class="outline-button" type="button">+ 新增分類</button></header>
    <p id="resource-error" class="form-message" role="alert"></p><ul id="resource-list" class="resource-list" aria-live="polite"></ul>
  </main>
</div>
<dialog id="resource-modal" class="entry-modal"><form id="resource-form" class="entry-modal__panel"><h2 id="resource-modal-title"></h2><label>名稱<input id="resource-name" maxlength="100" required></label><label id="resource-color-field">色彩<input id="resource-color" type="color" value="#e5533d" required></label><p id="resource-form-error" class="form-message" role="alert"></p><div class="entry-modal__actions"><button id="resource-cancel" class="outline-button" type="button">取消</button><button class="primary-button" type="submit">儲存</button></div></form></dialog>
@section Scripts { <script type="module" src="~/js/category.js" asp-append-version="true"></script> }
```

- [ ] **Step 2: Replace only the two applicable sidebar placeholders**

```cshtml
<a class="app-nav__link" asp-controller="Category" asp-action="Index"><span aria-hidden="true">◌</span>分類</a>
<a class="app-nav__link" asp-controller="Tag" asp-action="Index"><span aria-hidden="true">#</span>標籤</a>
```

Apply those links in Dashboard, TimeEntry, and the two new pages. In Tag's page shell, move `app-nav__link--active` and `aria-current="page"` from Category to Tag; keep Report and Settings `href="#"` links unchanged.

- [ ] **Step 3: Add focused styles with existing design tokens**

```css
.resource-content{padding-top:52px}.resource-list{display:grid;gap:10px;margin:0;padding:0;list-style:none}.resource-row{display:flex;align-items:center;gap:12px;padding:16px 18px;border:1px solid var(--line);border-radius:12px;background:var(--surface)}.resource-swatch{width:18px;height:18px;border-radius:50%;border:1px solid rgb(0 0 0 / 12%)}.resource-name{flex:1;font-weight:800}.resource-actions{display:flex;gap:6px}@media(max-width:720px){.resource-content{padding-top:34px}.resource-row{align-items:flex-start;flex-wrap:wrap}.resource-actions{width:100%;justify-content:flex-end}}
```

- [ ] **Step 4: Manually inspect the two page skeletons**

Run: `dotnet run --project ".\Lyubishchev Time Management.csproj"`

Expected: authenticated navigation reaches `/Category` and `/Tag`; desktop and 320px-wide mobile layouts keep actions visible and keyboard reachable.

- [ ] **Step 5: Commit view shell and navigation**

```bash
git add "Lyubishchev Time Management/Views/Category/Index.cshtml" "Lyubishchev Time Management/Views/Tag/Index.cshtml" "Lyubishchev Time Management/Views/Dashboard/Index.cshtml" "Lyubishchev Time Management/Views/TimeEntry/Index.cshtml" "Lyubishchev Time Management/wwwroot/css/category-tag.css"
git commit -m "feat: add category and tag management pages"
```

### Task 6: 實作管理頁 JavaScript 並驗收 UI 流程

**Files:**
- Create: `Lyubishchev Time Management/wwwroot/js/category.js`
- Create: `Lyubishchev Time Management/wwwroot/js/tag.js`

- [ ] **Step 1: Implement the shared interaction protocol in `category.js`**

```javascript
const page = document.querySelector('#category-page');
if (page) {
  const state = { items: [], editingId: null };
  const $ = (selector) => page.querySelector(selector);
  const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content ?? '';
  async function callApi(path, { method = 'GET', body } = {}) {
    const response = await fetch(path, { method, headers: { 'Content-Type': 'application/json', ...(method === 'GET' ? {} : { 'X-CSRF-TOKEN': csrf() }) }, body: body ? JSON.stringify(body) : undefined });
    const payload = await response.json().catch(() => null);
    if (!response.ok) throw new Error(payload?.detail ?? '發生錯誤，請稍後再試。');
    return payload;
  }
  async function refresh() { state.items = await callApi('/api/categories'); render(); }
}
```

Render `textContent` through DOM nodes for names rather than interpolating a user-supplied name into `innerHTML`. The form submits POST when `editingId` is null and PATCH otherwise; successful operations close the dialog and call `refresh`. DELETE first asks `window.confirm('刪除分類後，既有紀錄會改為未分類。確定刪除嗎？')`, then calls DELETE and refreshes. Display API `detail` in `#resource-form-error` or `#resource-error`.

- [ ] **Step 2: Implement `tag.js` with Tag-specific resource settings**

Copy the same flow but use `#tag-page`, `/api/tags`, and `{ name }`. Do not render or submit `resource-color-field`; its confirmation is exactly `刪除標籤後，既有紀錄將移除這個標籤。確定刪除嗎？`.

- [ ] **Step 3: Manually exercise the completed user flows**

Run: `dotnet run --project ".\Lyubishchev Time Management.csproj" --no-build`

Verify while logged in:

1. Create `Focus` category with a color, reload, edit both name/color, and delete it after assigning it to a TimeEntry; the entry remains uncategorized.
2. Try ` focus ` and confirm the UI surfaces the 409 detail rather than silently accepting it.
3. Create a Tag, attach it to a TimeEntry via inline entry editor, delete it from Tag management, and confirm the TimeEntry remains while that chip disappears on reload.
4. Tab through add, edit, cancel, save, and delete actions at desktop width and 320px viewport width.

- [ ] **Step 4: Run complete automated verification**

Run: `dotnet build ".\Lyubishchev Time Management.csproj" --no-restore`

Expected: `Build succeeded.`

Run: `dotnet test .\Tests\AuthFlow.Tests\AuthFlow.Tests.csproj --no-restore; dotnet test .\Tests\TimerFlow.Tests\TimerFlow.Tests.csproj --no-restore; dotnet test .\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore`

Expected: all three test projects PASS.

- [ ] **Step 5: Commit frontend behavior**

```bash
git add "Lyubishchev Time Management/wwwroot/js/category.js" "Lyubishchev Time Management/wwwroot/js/tag.js"
git commit -m "feat: manage categories and tags from the browser"
```

### Task 7: 文件同步與最終檢查

**Files:**
- Modify: `Lyubishchev Time Management/Docs/TODO.md`
- Modify: `Lyubishchev Time Management/Docs/HANDOFF.md`

- [ ] **Step 1: Mark only completed Category/Tag items**

In `Docs/TODO.md`, change the Category and Tag service/controller/API/page entries from `[ ]` to `[x]`; replace their existing `[~]` TimeEntry integration notes with `[x]` only after Task 3 regression tests pass. Do not mark Dashboard/Report aggregation work as complete.

- [ ] **Step 2: Update handoff with implementation facts**

Document the routes, `NormalizedName` uniqueness key, migration name, error codes, delete semantics, the exact tests run, and any migration preflight outcome. Do not paste secrets, connection strings, JWTs, or test database paths.

- [ ] **Step 3: Final repository checks**

Run: `git diff --check`

Expected: no output.

Run: `git status --short`

Expected: only the two documentation files before their commit.

- [ ] **Step 4: Commit documentation**

```bash
git add "Lyubishchev Time Management/Docs/TODO.md" "Lyubishchev Time Management/Docs/HANDOFF.md"
git commit -m "docs: record category and tag management"
```

## Plan self-review

- **Spec coverage:** Tasks 1–3 cover normalization, database uniqueness, CRUD, ownership, timestamp, delete semantics, and inline Tag reuse; Task 4 covers API, authorization and CSRF; Tasks 5–6 cover pages, navigation, RWD and user feedback; Task 7 keeps project handoff current.
- **No hidden work:** Each affected production/test/documentation file is named above; commands state expected outcomes and required migration review.
- **Type consistency:** Request/response and result names are consistently `Category*` and `Tag*`; persistence key is consistently `NormalizedName`; API error codes match the approved design.
