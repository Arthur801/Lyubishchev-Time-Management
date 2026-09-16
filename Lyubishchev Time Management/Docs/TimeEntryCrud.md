# Manual TimeEntry CRUD 實作說明

本文件說明 `AGENTS.md` Implementation Order 第 4 項「Manual TimeEntry CRUD」如何實作，包含設計思路、牽涉的檔案，以及每個檔案負責的內容。範圍涵蓋後端 CRUD（含 Category 指派與 Tag inline 建立）與完整前端（History List 頁面串接真實 API、新增/編輯表單、分頁）。

## 設計思路

- **重用 `TimerService`/`AuthService` 已建立的 `Result` record 模式**：`TimeEntryResult`/`TimeEntryDeleteResult` 皆為 `record`，帶 `Ok()`/`Fail(errorCode, errorMessage)` 靜態工廠，`TimeEntryApiController` 收到失敗結果後統一用 `Problem(detail, statusCode, title: errorCode)` 轉成對應的 HTTP 狀態碼，與 `TimerApiController`/`AccountController` 的錯誤處理慣例一致。
- **Category 只做「擁有權驗證」，不建立完整 CRUD**：`CategoryService`/`Controllers/Api/CategoryApiController.cs`（Implementation Order 第 5 項）仍是空殼。這次只在 `TimeEntryService` 內部加一個 `GetOwnedCategoryAsync(userId, categoryId)`，確認 TimeEntry 想指定的 CategoryId 確實屬於目前使用者，找不到就回傳 `CATEGORY_NOT_FOUND`（404，「不存在」與「別人的」回傳同一種結果，避免洩漏其他使用者資料是否存在）。History 頁面需要的「分類下拉選單」則透過 `TimeEntryService.GetCategoryOptionsAsync` 讀取，並在 `TimeEntryController.Index()` 组成 `HistoryViewModel` 交給 Razor 伺服器端渲染，刻意不新增 `/api/categories` 端點——那是第 5 項的範圍。
- **Tag 支援 inline find-or-create，比照 `TimerService.StartAsync` 的併發處理手法**：`(UserId, Name)` 是真正的唯一索引，兩個分頁/裝置同時建立同名新標籤時，`FindOrCreateTagsAsync` 會：先查已存在的標籤、對缺少的名稱嘗試 `AddRange` + `SaveChangesAsync`；若因唯一索引衝突丟出 `DbUpdateException`，就把這次新增的（失敗的）Tag 實體從追蹤器 detach，重新查一次資料庫——此時對方那筆已經 commit，重新查到的結果就能拿來用，不需要重試迴圈或例外重丟。詳見 `Tests/TimeEntryFlow.Tests` 的併發測試。
  - 標籤解析發生在「建立/更新 `TimeEntry` 本體之前」，且是獨立的一次 `SaveChangesAsync`：就算標籤那次寫入真的因為非預期原因失敗，也不會牽動使用者對 Name/Start/End/Category 的編輯（那些改動這時候都還沒進入變更追蹤器）。Create/Update 整體沒有包一層外顯的 DB Transaction——不像 `TimerService.StopAsync` 需要跨兩張表做「刪一筆、建一筆」的原子操作，TimeEntry CRUD 沒有對等的原子性需求；最壞情況只是多一筆沒人使用的孤兒標籤（Tag 本來就是可重複使用、對使用者可見的低成本資料，不是需要嚴格保護的機密狀態）。
- **`AGENTS.md` 明確允許 TimeEntry 重疊**（"TimeEntries may overlap... Overlapping durations are added directly"），所以 `CreateAsync`/`UpdateAsync` 完全沒有「檢查是否與其他紀錄重疊」的邏輯，只驗證 `EndTimeUtc > StartTimeUtc`。
- **`UpdateTimeEntryRequest` 對 Start/End 是真正的局部更新語意，對 Name/CategoryId/Tags 則是「整值取代」**：
  - `StartTimeUtc`/`EndTimeUtc`：`null` = 不更動、有值 = 取代。因為一筆 TimeEntry 的開始/結束時間永遠不可能合法地是 null，這兩個語意不會互相衝突。`UpdateAsync` 會用 `request.StartTimeUtc ?? entry.StartTimeUtc`（`EndTimeUtc` 同理）算出「有效值」再檢查 `> `，因此單獨 `PATCH { endTimeUtc }` 也會對照「目前資料庫裡的」`StartTimeUtc` 做驗證。
  - `Name`/`CategoryId`：每次都當成完整取代值，而不是「有給才改」。這是因為前端編輯表單每次都會把該筆紀錄目前的完整 Name/CategoryId 一併送出（即使沒有變更），所以在目前唯一的呼叫方（`time-entry.js`）之下不會有「漏了某欄位、結果被誤清空」的風險。**這是刻意的取捨，不是尚未處理的 bug**：如果之後有其他呼叫方只想單獨 PATCH 其他欄位（例如未來的「快速搬到別的分類」功能）、卻沒有一併帶上 `name`，`Name` 就會被清空成 `null`。目前沒有為此另外設計 tri-state（例如包一層 `Optional<T>` 包裝型別）的原因是：現在唯一的呼叫方不會踩到這個情況，且 `Optional<T>` 這類包裝在整個專案裡沒有先例，對單人使用的 V1 而言是不成比例的複雜度。**如果之後要新增其他呼叫方，请先確認它一定會帶上完整的 `name`/`categoryId`，或屆時再補上正式的 tri-state 設計。**
  - `Tags`：`null` = 不更動、`[]` = 清空所有標籤、非空陣列 = 整組取代。這裡沒有上述的歧義，因為 C# 的 `List<string>?` 可以合法區分「沒傳」（`null`）和「傳了空陣列」（`[]`），不需要額外設計。
- **`DeleteAsync` 用 `ExecuteDeleteAsync`（EF Core 的批次刪除），把「擁有權檢查」和「刪除」合成同一個帶 `WHERE UserId = ...` 的 SQL 陳述式**，不用先 `Load` 再 `Remove`。`TimeEntryTag` 關聯列由資料庫層級的 `ON DELETE CASCADE`（`TimeEntryTagConfiguration`）自動清掉，跟呼叫方式（change tracker 或批次刪除）無關。已驗證 `MySql.EntityFrameworkCore` 10.0.9 支援 `ExecuteDeleteAsync`。

## 前端範圍與時區的取捨

- **Category/Tag 篩選、搜尋、分頁**：History 頁面原本的篩選列（日期範圍/分類/搜尋）UI 保留，只把背後邏輯從「在瀏覽器記憶體裡過濾 8 筆假資料」改成呼叫 `GET /api/time-entries` 並帶上查詢參數；新增 `#pagination`（上一頁/下一頁 + 頁碼），預設每頁 50 筆（`AGENTS.md` 建議值）。
- **新增/編輯表單是全新功能**：原本的 mock 完全沒有新增/編輯 UI，只有「刪除」。這次在 `Views/TimeEntry/Index.cshtml` 新增一個 `<dialog id="entry-modal">`（原生 HTML `<dialog>`，`showModal()`/`close()`），欄位為活動名稱、開始/結束時間（`datetime-local`）、分類下拉（伺服器端渲染自 `HistoryViewModel.Categories`）、標籤編輯器（沿用 `wwwroot/js/timer.js` 已經有的標籤 chip 編輯互動：輸入 + Enter/按鈕加入、點 × 移除）。
- **日期範圍如何轉成 UTC 查詢參數**：目前完全用瀏覽器本地時區（`new Date()` + `.toISOString()`）計算「今天/本週/本月」的本地日界線，再轉成 UTC 送給後端；「全部」則兩個查詢參數都不帶，後端就不加時間篩選。這是刻意的簡化：`UserSettingsService`/時區設定頁（Implementation Order 第 12 項）還沒做，`User.TimeZoneId` 目前對所有使用者都是寫死的預設值，沒有任何 UI 可以修改它，此時投入「依使用者設定的時區換算」的邏輯是對一個還沒人用得到的設定做超前工程。這個簡化同時套用在「讀」（篩選範圍）與「寫」（新增/編輯表單的開始/結束時間）兩側，所以不會造成資料錯亂——唯一的風險是瀏覽器所在時區與帳號未來設定的時區不同時，「今天/本週/本月」篩選的日界線可能有時差，等第 12 項做完就會自動修正。

## 開發過程中透過真實伺服器測試抓到的兩個問題

在自動化測試（SQLite in-memory）全部通過之後，另外啟動一份本機開發伺服器（真實 MySQL）搭配 `curl` 手動跑過一次完整流程（註冊 → 建立→列表→篩選→部分更新→刪除），過程中發現並修正了一個自動化測試沒抓到的真實 bug：

- **`UpdateAsync` 忘記 `.ThenInclude(link => link.Tag)`**：原本只有 `.Include(e => e.TimeEntryTags)`，在「這次 PATCH 沒有帶 `tags`（維持原標籤）」的分支裡讀取 `entry.TimeEntryTags.Select(link => link.Tag.Name)` 時，`link.Tag` 是 `null`，丟出 `NullReferenceException`（對應到 500）。
  - **自動化測試當初為什麼沒抓到**：`Tests/TimeEntryFlow.Tests` 裡最初的測試在同一個 `DbContext` 執行個體裡先 `CreateAsync`（把 `Tag` 實體加入變更追蹤器）再呼叫 `UpdateAsync`，EF Core 的追蹤器 identity-fixup 機制會自動幫已經追蹤的 `Tag` 實體把導覽屬性接上，掩蓋了缺少 `Include` 的問題。真正的 HTTP 請求每次都是全新的、scoped 的 `DbContext`，就不會有這種「巧合補上」的效果。
  - **修正**：`Services/TimeEntryService.cs` 的 `UpdateAsync` 查詢改成 `.Include(e => e.TimeEntryTags).ThenInclude(link => link.Tag)`。
  - **同時修正測試**：`UpdateAsync_omitting_tags_leaves_existing_tags_untouched` 改成用兩個獨立的 `AppDbContext`（建立用一個、更新用另一個），模擬真實的「每個請求一個新 DbContext」情境，這樣以後如果又不小心漏掉 `Include`，測試會直接抓到，不會再被追蹤器的巧合掩蓋。
- **SQLite 的 EF Core provider 不支援對 `ulong` 欄位做 `ORDER BY`**：`ListAsync` 原本用 `.ThenByDescending(e => e.Id)` 當 `StartTimeUtc` 相同時的 tie-break，這在 SQLite provider 上會直接丟 `NotSupportedException`（MySQL 不會，因為 MySQL 的 provider 有對 unsigned 整數排序的支援）。改成 `.ThenByDescending(e => (long)e.Id)`——對這個應用程式而言，`TimeEntry.Id` 永遠不可能大到超出 `long` 的範圍，所以這個轉型是安全的，同時解決了可攜性問題（SQLite 測試與正式的 MySQL 都能用同一段程式碼）。

這兩個問題再次印證：`Tests/TimerFlow.Tests`、`Tests/TimeEntryFlow.Tests` 選用 SQLite 而非 EF Core InMemory provider（因為需要真正的交易/唯一索引/外鍵行為）是對的方向，但 SQLite 終究不是 MySQL——兩個問題都是「SQLite 測試全過，換成真正的關聯式資料庫語意/請求生命週期才會現形」的類型。之後如果有餘力，針對正式環境的 MySQL 建一次性的手動或 CI 驗證會更保險。

## 順帶修正：主專案 `.csproj` 缺少 `Tests\**` 的 Content/None 排除

在跑上述真實伺服器測試的過程中，`dotnet build`/`dotnet test` 越跑越慢（最後一次單純的 `dotnet build` 花了超過 4 分鐘，`du -sh` 想確認資料夾大小甚至跑不完），追查後發現：`Lyubishchev Time Management.csproj` 原本只有 `<Compile Remove="Tests\**\*.cs" />`，只排除了 C# 原始碼編譯，但 `Microsoft.NET.Sdk.Web` 對 `Content`/`None` 項目有隱含的 `**` 萬用字元規則。因為三個測試專案（`Tests/AuthFlow.Tests`、`Tests/TimerFlow.Tests`、`Tests/TimeEntryFlow.Tests`）都放在主專案目錄底下，且各自的 `bin/`（透過 `ProjectReference`）都含有主專案建置輸出的一份複本，主專案的隱含 `Content`/`None` glob 就會把「測試專案的 `bin` 資料夾」當成自己的內容項目一併複製進自己的建置輸出——而那份複本裡又包含了測試專案自己更早一次建置的 `bin`，於是每建置一次，巢狀深度就多一層，資料夾樹以指數速度爆炸（實測已經深到 `rm -rf` 光刪除就要跑好幾分鐘）。

修正方式：在主專案的 `.csproj` 補上 `<Content Remove="Tests\**" />` 與 `<None Remove="Tests\**" />`（連同原本的 `<Compile Remove="Tests\**\*.cs" />` 一起放寬成 `Tests\**`），讓整個 `Tests` 資料夾對主專案而言完全不存在，不只是排除 `.cs` 檔案。修正後，主專案與三個測試專案的建置/測試都恢復到一秒等級的正常速度。已爆炸的舊 `bin`/`obj` 資料夾（未被 Git 追蹤，`.gitignore` 已排除）已手動清除。

## 檔案清單與內容

### `Models/Responses/`

| 檔案 | 內容 |
|---|---|
| `PagedResult.cs` | 新增。`record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)`，`GET /api/time-entries` 的分頁信封。 |
| `TimeEntryResponse.cs` | 擴充既有型別，新增 `CategoryName`、`CategoryColor`、`Tags`，讓 History 列表不用為了顯示分類顏色/標籤 chip 再多打一次 API。因為這個型別也被 `TimerService.StopAsync` 共用，該處的建構呼叫一併補上 `CategoryName: null, CategoryColor: null, Tags: []`（計時器剛停止時還沒有分類/標籤）。 |

### `Models/Requests/`

| 檔案 | 內容 |
|---|---|
| `CreateTimeEntryRequest.cs` | `Name`（`[StringLength(500)]`）、`StartTimeUtc`/`EndTimeUtc`（`DateTime?` + `[Required]`，宣告成 nullable 是為了讓 `[Required]` 真的能在缺值時觸發）、`CategoryId`、`Tags`。 |
| `UpdateTimeEntryRequest.cs` | 同上欄位，全部可省略；語意見上方「設計思路」。 |

### `Models/ViewModels/HistoryViewModel.cs`

- 新增 `HistoryViewModel(IReadOnlyList<CategoryOption> Categories)` 與 `CategoryOption(ulong Id, string Name, string Color)`，供 `TimeEntryController.Index()` 與 `Views/TimeEntry/Index.cshtml` 使用。

### `Services/TimeEntryService.cs`

- 從空殼補上完整實作：`ListAsync`（分頁 + 日期範圍 overlap 篩選 + 分類/搜尋篩選）、`CreateAsync`、`UpdateAsync`、`DeleteAsync`、`GetCategoryOptionsAsync`，以及內部的 `GetOwnedCategoryAsync`、`NormalizeTagNames`、`FindOrCreateTagsAsync`。
- `ListAsync` 的日期範圍篩選直接套用 `AGENTS.md` Query Rules 的 Calendar overlap 條件（`Entry.StartTimeUtc < RangeEndUtc AND Entry.EndTimeUtc > RangeStartUtc`），且 `rangeStartUtc`/`rangeEndUtc` 為 `null` 時該筆條件完全不加入查詢——**刻意不用 `DateTime.MinValue`/`MaxValue` 當「無邊界」的哨兵值**，因為 `DateTime.MinValue`（0001-01-01）超出 MySQL `DATETIME` 欄位的合法範圍（最早支援到 1000-01-01），拿去當查詢參數會出錯。

### `Controllers/Api/TimeEntryApiController.cs`

- 從空殼補上 `[Authorize]` 的 `ControllerBase`，路由 `api/time-entries`：
  - `GET`：查詢參數 `startUtc`/`endUtc`/`categoryId`/`search`/`page`/`pageSize`（預設 `page=1`、`pageSize=50`），回傳 `PagedResult<TimeEntryResponse>`。
  - `POST`：`[ValidateAntiForgeryToken]`，`ModelState` 驗證後呼叫 `CreateAsync`，成功回傳 200 + `TimeEntryResponse`（沿用 `TimerApiController.Start` 的慣例，用 200 而非 201）。
  - `PATCH {id}`：`[ValidateAntiForgeryToken]`，呼叫 `UpdateAsync`。
  - `DELETE {id}`：`[ValidateAntiForgeryToken]`，呼叫 `DeleteAsync`，成功回傳 204，失敗回傳 404。
  - `MapFailure`：把 service 回傳的 errorCode 對應到 HTTP 狀態碼（`INVALID_TIME_RANGE` → 400、`CATEGORY_NOT_FOUND`/`TIME_ENTRY_NOT_FOUND` → 404）。
- 沒有 `GET /api/time-entries/{id}`：`AGENTS.md` 的端點清單本來就沒有這一條，編輯表單直接從前端已經拿到的列表資料裡取值即可，不需要多一次往返。

### `Controllers/TimeEntryController.cs`

- 從只回傳空白 View 的殼，改成呼叫 `TimeEntryService.GetCategoryOptionsAsync` 並組出 `HistoryViewModel` 交給 View。

### `Program.cs`

- 新增 `builder.Services.AddScoped<TimeEntryService>();`。

### `Views/TimeEntry/Index.cshtml`

- 加上 `@model HistoryViewModel`；分類篩選下拉與新增/編輯表單的分類下拉都改成從 `Model.Categories` 伺服器端渲染 `<option>`。
- 新增「+ 新增紀錄」按鈕、`<dialog id="entry-modal">`（新增/編輯共用同一個表單，依 `openModal('create' | 'edit', entry?)` 決定標題與初始值）、`<nav id="pagination">`。
- 既有的篩選列、`#entry-groups`、`#empty-state` 標記維持不變。

### `wwwroot/css/history.css`

- 新增 `.pagination`、`.entry-modal`（含 `::backdrop`）、`.entry-modal__panel`、`.entry-modal__time-fields`、`.entry-modal__actions` 等樣式，沿用既有的 `--surface`/`--line`/`--ink`/`--muted`/`--navy` 等 CSS 變數（定義在 `dashboard.css`，History 頁面本來就有載入），以及既有的 `.tag-list`/`.tag-chip`/`.add-tag`/`.field-label`/`.form-message`/`.outline-button` 類別（定義在 `dashboard.css`，最初是為了 Dashboard 計時器卡片而寫，這裡直接重用，沒有另外重造一套）。

### `wwwroot/js/time-entry.js`

- 整份重寫。移除寫死的 `seedEntries` 與 `CATEGORY_COLORS` 對照表；`state` 改為 `{ range, categoryId, search, page, pageSize, items, totalCount }`。
- `callApi()` 沿用 `timer.js` 已經驗證過的寫法（非 GET 帶 `X-CSRF-TOKEN`、解析 JSON、失敗時丟出帶 `detail` 訊息的 `Error`）。
- `getUtcRangeForPreset()` 負責把「今天/本週/本月/全部」轉成 UTC 查詢參數（見上方時區小節）。
- `fetchEntries()` 呼叫 `GET /api/time-entries`，並在目前頁碼超過新的總頁數時（例如剛好刪除了最後一頁的最後一筆）自動把頁碼收斂到有效範圍再重新查一次。
- 刪除按鈕改呼叫真正的 `DELETE`，成功後 `fetchEntries()` 重新拉取（而不是直接砍本地陣列），確保分頁/總筆數持續正確。
- 新增/編輯共用 `openModal(mode, entry?)`：編輯模式直接從 `state.items` 裡已經有的那筆資料取值預填，不必再打一次 API；送出時一律用 `Number(...)` 把分類 select 的字串值轉成數字再放進 JSON body（避免 `ulong?` 欄位收到 JSON 字串而綁定失敗）。

### `Tests/TimeEntryFlow.Tests/`（新增的測試專案）

比照 `Tests/AuthFlow.Tests`、`Tests/TimerFlow.Tests` 的慣例，獨立成一個 xUnit 專案：

- `TimeEntryFlow.Tests.csproj`：用 `Microsoft.EntityFrameworkCore.Sqlite`（而非 `AuthFlow.Tests` 用的 InMemory provider），因為 Tag find-or-create 的併發測試需要真正的唯一索引違反，Category/Tag 刪除的測試需要真正的外鍵行為（`ON DELETE SET NULL`/`CASCADE`），這些 EF Core InMemory provider 都不支援。同樣把 `SQLitePCLRaw.bundle_e_sqlite3` 釘選在 `2.1.12`（`TimerFlow.Tests` 已經因為同一個安全性公告 GHSA-2m69-gcr7-jv3q 做過這件事）。
- `TestClock.cs`：從 `TimerFlow.Tests` 複製，`IClock` 的可控測試替身。
- `Integration/TimeEntryServiceTests.cs`：16 個測試，涵蓋：
  - Create：允許重疊、拒絕 `end <= start`、拒絕別人的 CategoryId、標籤 find-or-create（新建 + 重用）、標籤 find-or-create 併發（`Task.WhenAll` 對同一個新標籤名稱真正平行呼叫兩次 `CreateAsync`，斷言只有一筆 Tag、兩筆 TimeEntry 都連到它）。
  - Update：局部更新仍正確驗證 `end > start`、拒絕更新別人的紀錄、省略 `Tags` 維持原標籤（用兩個獨立 `DbContext` 模擬真實請求情境）、`Tags: []` 清空標籤。
  - Delete：只能刪自己的紀錄，刪別人的回傳 `TIME_ENTRY_NOT_FOUND` 且不影響原資料。
  - 直接對 `DbContext` 操作驗證：刪除 Category 後 TimeEntry 保留且 `CategoryId` 變 `null`；刪除 Tag 後只移除關聯列、TimeEntry 本體不受影響。
  - List：涵蓋跨午夜邊界的 overlap 篩選（23:00→01:00 的紀錄應該被隔天 00:00–24:00 的查詢範圍抓到，另一筆完全落在範圍外的紀錄應該被排除）、分頁與總筆數、依分類/搜尋篩選、新到舊排序。
  - 併發測試本機重複執行 5 次未出現不穩定的情況。

## 尚未涵蓋的部分

- **Category/Tag 的完整管理頁面**（建立、改名、刪除分類/標籤）仍是 Implementation Order 第 5、6 項，尚未開始；這次只做了「讀取現有分類清單」與「標籤 inline 建立」。
- **`UpdateTimeEntryRequest` 對 `Name`/`CategoryId` 是整值取代而非嚴格局部更新**：已在「設計思路」段落說明原因與風險範圍，之後如果有除了目前這個編輯表單以外的呼叫方，需要重新評估是否要補上正式的 tri-state 設計。
- **Dashboard 統計卡片/圖表仍是 mock data**：這次串接的是 History 列表，不是 Dashboard 的彙總統計；那部分要等 `TimeAggregationService`/`DashboardService`（第 9、10 項）完成才會顯示真實資料。
- **CSV 匯出**（`GET /api/time-entries/export`，第 13 項）雖然跟這次的路由前綴相同，但不在這次範圍內，尚未實作。
- **時區換算**：如前述，目前用瀏覽器本地時區簡化處理，待 Settings/timezone（第 12 項）完成後需要一併檢視。
