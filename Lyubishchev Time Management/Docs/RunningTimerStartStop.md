# RunningTimer Start/Stop 核心計時功能實作說明

本文件說明 `AGENTS.md` Implementation Order 第 3 項「RunningTimer / Start / Stop」如何實作，包含設計思路、牽涉的檔案，以及每個檔案負責的內容。

## 設計思路

- **RunningTimer 只存 `UserId` + `StartedAtUtc`**，完全比照 `AGENTS.md` 的 Domain Rules：不在計時中儲存 Name/Category/Tags，這些欄位只存在於 Stop 之後產生的 `TimeEntry`。因此 Dashboard 上的「活動名稱」輸入框在計時期間只是前端本地狀態，直到呼叫 Stop 時才會把目前輸入的名稱一併送給後端，寫進新建立的 `TimeEntry.Name`。
- **`RunningTimers.UserId` 是主鍵**：`AGENTS.md` 要求「每個使用者最多一個 RunningTimer」，用主鍵天然保證唯一性，不需要額外的唯一索引或應用層鎖。
- **Start 採用「先查再寫，寫入衝突視為已在執行」的模式**：與 `AuthService.RegisterAsync` 處理重複 Email 的手法一致——先 `AnyAsync` 檢查，若通過但併發的另一個請求搶先寫入，`SaveChangesAsync` 會因為主鍵重複丟出 `DbUpdateException`，這裡一併接住並回傳 `TIMER_ALREADY_RUNNING`（409），不需要顯式鎖。
- **Stop 用「交易 + EF Core 內建的受影響筆數檢查」取代資料庫特定的 `SELECT ... FOR UPDATE`**：
  - `AGENTS.md` 要求 Stop 必須是一個交易：鎖定/讀取 RunningTimer → 讀取目前 UTC 時間 → 建立 TimeEntry → 刪除 RunningTimer → commit，任何一步失敗就 rollback。
  - 一開始的實作直接用 `FromSqlInterpolated("... FOR UPDATE")` 做悲觀鎖，這是 MySQL 語法。但這樣完全無法用任何一種可攜的 EF Core 測試替身驗證（EF Core InMemory provider 不支援 `Database.BeginTransactionAsync`，SQLite 也不支援 `FOR UPDATE` 語法），等於「多裝置同時 Stop 只會建立一筆 TimeEntry」這個最重要的規則完全沒有自動化測試覆蓋。
  - 改用「交易內先讀（不加鎖 hint）→ 準備好新增 TimeEntry／刪除 RunningTimer → `SaveChangesAsync`」。EF Core 對每一個 UPDATE/DELETE 都會檢查資料庫實際受影響的筆數是否等於預期（1 筆）；如果 RunningTimer 那筆列已經被另一個並發交易刪除並 commit，這裡的 DELETE 會影響 0 筆，EF Core 會丟出 `DbUpdateConcurrencyException`。我們把它視為「已經沒有計時器可停止」，回傳 `TIMER_NOT_RUNNING`（404），並讓 `await using` 的交易物件在沒有呼叫 `CommitAsync()` 的情況下自動 rollback（等同已停止呼叫的 TimeEntry Insert 也一併撤銷，不會產生「贏家已刪除、輸家仍新增了一筆 TimeEntry」的情況）。
  - 這個機制不依賴任何資料庫專屬語法，只依賴標準 SQL 語意（對已不存在的資料列下 DELETE，回報 0 筆受影響）與 EF Core 所有關聯式 provider 共通的受影響筆數檢查，因此可以用 MySQL（正式環境）與 SQLite（測試環境）驗證同一套邏輯，正確性等價於顯式的悲觀鎖。
- **`EndTimeUtc > StartTimeUtc` 的防呆**：`TimeEntries` 有對應的 CHECK 約束。如果 Start 與 Stop 發生在同一個時脈刻度（例如自動化測試連續呼叫、系統時鐘解析度不夠），`clock.UtcNow` 可能等於 `StartedAtUtc`，這裡在 Stop 時多做一個 `endTimeUtc <= StartedAtUtc → endTimeUtc = StartedAtUtc.AddTicks(1)` 的防呆，避免違反 DB 約束造成非預期的 500 錯誤。
- **`IClock` 抽象化系統時間**：`TimerService` 不直接呼叫 `DateTime.UtcNow`，而是透過建構子注入的 `IClock`。這讓測試可以完全控制「現在幾點」，才能寫出「開始 10:00、經過 25 分鐘後停止」這種決定性（deterministic）的測試，也符合 `Infrastructure/Clock/` 目錄本來就規劃要用來抽象時間的定位。
- **Stop 只接受可選的 `Name`，不接受 Category/Tags**：`Category`/`Tag` 對應的 `CategoryService`/`TagService`（Implementation Order 第 5、6 項）都還是空殼，無法驗證 CategoryId 屬於目前使用者、也無法建立 TimeEntryTag 關聯。為了不引入尚未存在的相依性，Stop 目前只把 Name 寫入 TimeEntry，Category/Tags 留給之後的 Manual TimeEntry CRUD（第 4 項）與 Category/Tag CRUD 完成後，再讓使用者編輯已產生的紀錄。

## 整體流程

```text
1. 使用者造訪 /Dashboard
   → timer.js 呼叫 GET /api/timer 取得目前計時器狀態
   → 若已有人在其他裝置啟動計時器，這裡會顯示「正在記錄」與已經過的時間

2. 使用者按下「開始計時」
   → timer.js 呼叫 POST /api/timer/start（帶 CSRF header）
   → TimerService.StartAsync：
     → 查詢是否已有 RunningTimer → 有：回傳 409 TIMER_ALREADY_RUNNING
     → 沒有：新增 RunningTimer { UserId, StartedAtUtc = now }
       → 若因併發被別的請求搶先建立（主鍵衝突）：同樣回傳 409 TIMER_ALREADY_RUNNING
   → 前端記錄 StartedAtUtc，每秒用本地時間重新計算已經過秒數更新畫面

3. 使用者按下「停止計時」（可能是別台裝置）
   → timer.js 讀取目前「活動名稱」輸入框的值，呼叫 POST /api/timer/stop { name }
   → TimerService.StopAsync（單一交易）：
     → 讀取目前使用者的 RunningTimer → 沒有：回傳 404 TIMER_NOT_RUNNING
     → 計算 EndTimeUtc（並確保晚於 StartedAtUtc）
     → 新增 TimeEntry、刪除 RunningTimer
     → SaveChangesAsync：
       → 成功 → CommitAsync → 回傳建立好的 TimeEntry 資訊
       → 刪除受影響筆數為 0（代表被別的裝置搶先 Stop）→ DbUpdateConcurrencyException
         → 回傳 404 TIMER_NOT_RUNNING，交易隨 using 區塊結束自動 rollback
   → 前端清空計時畫面與名稱輸入框
```

## 檔案清單與內容

### `Infrastructure/Clock/`

| 檔案 | 內容 |
|---|---|
| `IClock.cs` | 定義 `DateTime UtcNow { get; }`。 |
| `SystemClock.cs` | 唯一的正式實作，直接回傳 `DateTime.UtcNow`；以 Singleton 註冊進 DI。測試改用假的 `IClock` 讓時間可控。 |

### `Models/Responses/`

| 檔案 | 內容 |
|---|---|
| `TimerResponse.cs` | `record TimerResponse(bool IsRunning, DateTime? StartedAtUtc)`，`GET /api/timer` 與 `POST /api/timer/start` 共用。 |
| `TimeEntryResponse.cs` | `record TimeEntryResponse(ulong Id, string? Name, DateTime StartTimeUtc, DateTime EndTimeUtc, ulong? CategoryId, long DurationSeconds)`，`POST /api/timer/stop` 的回傳內容；`CategoryId` 目前恆為 `null`（Category 尚未串接），欄位保留是因為它已經是 `TimeEntry` 實體既有的欄位，之後 Manual TimeEntry CRUD（第 4 項）也會回傳同一種形狀，不需要另外重新設計。 |

### `Models/Requests/StopTimerRequest.cs`

- 新增檔案：`{ string? Name }`，`[StringLength(500)]` 對齊 `TimeEntry.Name` 的欄位長度限制。

### `Services/TimerService.cs`

- 從空殼補上完整實作，包含 `TimerResult`/`StopTimerResult` 兩個回傳型別（模式與 `AuthService.AuthResult` 一致：成功/失敗 + errorCode + errorMessage）。
- `GetStatusAsync`：`AsNoTracking` 查詢目前使用者的 RunningTimer，轉成 `TimerResponse`。
- `StartAsync`：見上方設計思路。
- `StopAsync`：見上方設計思路，內含交易管理、EndTime 防呆、`DbUpdateConcurrencyException` 處理。

### `Controllers/Api/TimerApiController.cs`

- 從空殼補上 `[Authorize]` 保護的 `ControllerBase`，路由 `api/timer`：
  - `GET`：回傳 `TimerResponse`。
  - `POST start`：`[ValidateAntiForgeryToken]`，成功回傳 `TimerResponse`，衝突回傳 409 + `ProblemDetails`（`title = "TIMER_ALREADY_RUNNING"`）。
  - `POST stop`：`[ValidateAntiForgeryToken]`，`[FromBody] StopTimerRequest`，先檢查 `ModelState`，成功回傳 `TimeEntryResponse`，找不到執行中的計時器回傳 404 + `ProblemDetails`（`title = "TIMER_NOT_RUNNING"`）。
- 都透過 `CurrentUserService.GetRequiredUserId()` 取得使用者 ID，不接受前端傳入的 UserId，符合 `AGENTS.md` 的 ownership 規則。

### `Program.cs`

- 註冊 `IClock`/`SystemClock`（Singleton）與 `TimerService`（Scoped，因為它相依 `AppDbContext`）。

### `wwwroot/js/timer.js`

- 從空檔案補上完整實作，是 Dashboard 計時卡片的唯一擁有者（load 時查詢狀態、開始/停止呼叫真正的 API、每秒用本地時間更新畫面）。
- 標籤（Tag）的加入/移除 UI 維持純前端狀態，**尚未送到後端**，因為 Tag CRUD（Implementation Order 第 6 項）還沒實作；等該項完成後，Stop 呼叫需要再擴充成同時建立 `TimeEntryTag` 關聯。
- 分類（Category）下拉選單目前完全沒有被 `timer.js`讀取或使用，原因相同（Category CRUD 是第 5 項，尚未實作）。

### `wwwroot/js/dashboard.js`

- 移除原本用來「模擬」計時器的程式碼（`timerRunning`/`elapsedSeconds`/`interval`/`startedAt`、`renderTimer`、`renderTagsEditor`、`stopTimer`，以及 `#timer-toggle`/`#add-tag`/`#tag-input`/`#timer-tags` 的事件監聽），改由 `timer.js` 負責。
- `dashboard.js` 現在只處理日期範圍切換與統計圖表（總時間、分類圓餅圖、標籤長條圖、每日趨勢、最近活動），這部分**仍是前端 mock data**（`dashboard-state.mjs`），要等 `TimeAggregationService`/`DashboardService`（Implementation Order 第 9、10 項）完成才會串接真實資料。
- 刻意不把 Stop 產生的真實 `TimeEntry` 塞進 mock 的統計快照（`addCompletedEntry`）：那樣會讓一筆真實資料悄悄混進假資料算出來的圖表，造成「看起來像真的、其實是假的」的誤導。在 Dashboard 統計串接真實後端之前，計時器（真實）與統計卡片/圖表（mock）刻意保持互不相干。
- `dashboard-state.mjs` 完全沒有變動，`addCompletedEntry` 仍保留 export（`Tests/Unit/dashboard-frontend.test.mjs` 直接測試這個函式）。

### `Views/Dashboard/Index.cshtml`

- `@section Scripts` 新增 `<script type="module" src="~/js/timer.js">`，放在 `dashboard.js` 之前載入。畫面本身的 HTML/ID 完全沒有變動（沿用既有的 `timer-card`/`timer-toggle`/`timer-display` 等元素）。

### `Tests/TimerFlow.Tests/`（新增的測試專案）

比照既有的 `Tests/AuthFlow.Tests`，為 RunningTimer 這個功能建立獨立的 xUnit 測試專案：

- `TimerFlow.Tests.csproj`：`ProjectReference` 指回主專案，並使用 `Microsoft.EntityFrameworkCore.Sqlite`（而非 `AuthFlow.Tests` 用的 `Microsoft.EntityFrameworkCore.InMemory`）。原因是 `TimerService.StopAsync` 需要 `Database.BeginTransactionAsync`，EF Core InMemory provider 不支援關聯式 provider 專屬的 API（會直接丟例外），SQLite 則是真正的關聯式資料庫，可以完整驗證交易與並發語意。
- `TestClock.cs`：`IClock` 的測試替身，`UtcNow` 是可讀寫屬性，讓測試可以精準控制「現在幾點」與「經過多久」。
- `Integration/TimerServiceTests.cs`：
  - `CreateSharedDatabaseAsync()`：建立一個具名的 SQLite shared-cache in-memory 資料庫（`file:<guid>?mode=memory&cache=shared`），並保留一個「keep-alive」連線讓資料庫在整個測試期間存活；同時預先寫入一筆 `User`，因為 SQLite（不像 EF Core InMemory）會真的檢查 `RunningTimers`/`TimeEntries` 對 `Users` 的外鍵約束。
  - 涵蓋 `AGENTS.md` Testing Priorities 的「Timer state」與「Start/Stop Timer、concurrent Stop behavior」：
    - Start 在沒有計時器時成功、在已有計時器時回傳 `TIMER_ALREADY_RUNNING`。
    - `GetStatusAsync` 正確反映開始前/後的狀態。
    - Stop 在沒有計時器時回傳 `TIMER_NOT_RUNNING`。
    - Stop 成功時正確建立 `TimeEntry`（含 trim 過的 Name、正確的 Start/End 時間、`DurationSeconds`）並清除 RunningTimer。
    - Stop 在時鐘沒有前進的極端情況下仍保證 `EndTimeUtc > StartTimeUtc`。
    - **並發 Stop 測試**：用 `Task.WhenAll` 讓兩個各自獨立的 `DbContext`（模擬兩台裝置）對同一筆 RunningTimer 真正平行呼叫 `StopAsync`，斷言恰好一個成功、一個回傳 `TIMER_NOT_RUNNING`，且資料庫最終只有一筆 `TimeEntry`、零筆 `RunningTimer`。本機重複執行 5 次未出現不穩定（flaky）的情況。
  - 額外將 `SQLitePCLRaw.bundle_e_sqlite3` 明確釘選到 `2.1.12`（`Microsoft.EntityFrameworkCore.Sqlite 10.0.9` 預設帶入的 `2.1.11` 有已知的高風險安全性公告 GHSA-2m69-gcr7-jv3q）。

## 尚未涵蓋的部分

- **Category/Tags 尚未接到 Stop**：如上所述，等 Implementation Order 第 5、6 項（CategoryService/TagService）完成後，需要擴充 `StopTimerRequest`（或改為在 Stop 之後另外呼叫 TimeEntry 編輯 API）才能把分類與標籤一併寫入。
- **Dashboard 統計卡片/圖表仍是 mock data**：計時器本身已經是真實資料，但 Dashboard 其餘部分（總時數、分類圓餅圖、標籤長條圖、每日趨勢、最近活動）要等 `TimeAggregationService`/`DashboardService`（第 9、10 項）完成才會顯示真實資料。
- **並發 Stop 測試使用 SQLite 而非 MySQL**：SQLite 是資料庫層級鎖（whole-database lock），MySQL InnoDB 是列鎖（row lock），兩者鎖的粒度不同；但本文件驗證的性質是「DELETE 命中 0 筆時 EF Core 會丟出並發例外」這個與鎖粒度無關的標準行為，因此測試結論仍然適用於 MySQL。若要百分之百貼近正式環境，之後可以另外針對真正的 MySQL 執行個體補上一次性的手動或 CI 整合測試。
