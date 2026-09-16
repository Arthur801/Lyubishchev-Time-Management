# 專案交接摘要（給接手的 AI Agent）

最後更新：2026-09-17，涵蓋到 commit `74090e8`（"Implement Manual TimeEntry CRUD with Category/Tag support"）。本文件的目的是讓另一個 AI agent 不需要重新爬梳整個對話記錄，就能接續目前的進度。**開始工作前務必先讀 `AGENTS.md`（專案根目錄，`CLAUDE.md` 只是 `@AGENTS.md` 的轉介）——那是這個專案唯一的權威規格文件，所有設計決策都必須對齊它。**

---

## 專案是什麼

「Lyubishchev Time Management」：單人使用的時間紀錄網站（仿柳比歇夫時間管理法）。技術棧：ASP.NET Core MVC（.NET 10）+ EF Core + MySQL（正式環境）、Razor Views + 原生 JS（無前端框架）、JWT 存在 HttpOnly Cookie、AWS EC2 + Nginx 部署（尚未做）。專案哲學明確要求「先求小而完整，不要做團隊協作/AI 助理/訂閱制等超出範圍的功能」。

專案路徑：`C:\Users\Arthr801\source\repos\Lyubishchev Time Management\Lyubishchev Time Management`（注意：repo 根目錄和專案目錄同名且有空格，`.csproj` 路徑要整段加引號）。這個環境裡 Bash 工具實際跑在 **WSL** 裡（不是原生 Git Bash），PowerShell 工具則是原生 Windows；兩者看到的行程 PID 空間不完全一致，混用時要小心。

---

## 目前進度（對照 `AGENTS.md` 的 Implementation Order）

**唯一的即時真相來源是 [`Docs/TODO.md`](TODO.md)**——每完成一個項目都要回去更新它（含子項目的 `[x]`/`[~]`/`[ ]` 標記、「已完成項目摘要」、「下一步建議優先順序」）。以下是摘要，細節與設計理由都在各自的 Docs 文件裡：

| # | 項目 | 狀態 | 說明文件 |
|---|---|---|---|
| 1 | Project / MySQL / EF Core | ✅ 完成 | schema 已定案，之後不太需要動 |
| 2 | User / Register / Login / Logout / JWT | ✅ 完成 | [`Docs/JWT.md`](JWT.md) |
| 3 | RunningTimer / Start / Stop | ✅ 完成 | [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md) |
| 4 | Manual TimeEntry CRUD | ✅ 完成 | [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md) |
| 5 | Category | 🟡 只做了唯讀查詢+擁有權驗證（供第4項用），完整管理頁面/CRUD API 未做 | 見下方「下一步」 |
| 6 | Tag + TimeEntryTag | 🟡 只做了 inline find-or-create（供第4項用），完整管理頁面/CRUD API 未做 | 見下方「下一步」 |
| 7 | History List | ✅ 完成（隨第4項一起做，非 mock data） | [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md) |
| 8 | Calendar View | ❌ 未開始 | — |
| 9 | TimeAggregationService | ❌ 未開始 | — |
| 10 | Dashboard | 🟡 頁面殼 + 計時器卡片是真資料，其餘統計卡片/圖表仍是前端 mock（`dashboard-state.mjs`） | — |
| 11 | Report | ❌ 未開始 | — |
| 12 | Timezone settings | ❌ 未開始（`User.TimeZoneId` 全部寫死預設值，無 UI 可改） | — |
| 13 | CSV export | ❌ 未開始 | — |
| 14 | RWD | 🟡 各頁面 CSS 內建 media query，但沒有集中在 `responsive.css` | — |
| 15 | Error handling / Logging / Rate limit | 🟡 Rate limiting 與 CSRF 已完成；global exception handler 未確認；`Infrastructure/Logging` 只有 auth 事件 | [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md) |
| 16 | Nginx / EC2 / Backup | ❌ 未開始 | — |

**建議下一步優先順序**（`Docs/TODO.md` 目前寫的）：
1. Category、Tag 完整管理頁面（`CategoryService`/`TagService` + 對應 Controller/View/Api）
2. `TimeAggregationService` + Dashboard/Report（讓 Dashboard 統計卡片/圖表串接真實資料）
3. 全域例外處理與其餘 `Infrastructure/Logging` 事件（DB 錯誤、timer transaction failure、CSV export failure）

---

## 已建立的架構慣例（新功能請比照辦理，不要另創一套）

這些慣例是在做第 2～4 項的過程中逐步確立的，具有約束力，之後每一項都應該延續：

1. **Service 回傳 `Result` record**：`sealed record XResult(bool Succeeded, TPayload? Payload, string? ErrorCode, string? ErrorMessage)`，帶 `Ok()`/`Fail(errorCode, errorMessage)` 靜態工廠。範例：`AuthResult`（`AuthService.cs`）、`TimerResult`/`StopTimerResult`（`TimerService.cs`）、`TimeEntryResult`/`TimeEntryDeleteResult`（`TimeEntryService.cs`）。
2. **Controller 很薄**：驗證 `ModelState`（失敗回 `ValidationProblem(ModelState)`）→ 呼叫 Service → 依 `ErrorCode` 映射 HTTP 狀態碼（`Problem(detail, statusCode, title: errorCode)`）。`errorCode` 就是 `ProblemDetails.title`，前端統一從 `payload.detail` 讀錯誤訊息文字。**Controller 不直接碰 `AppDbContext`**——就連「查一個下拉選單用的清單」這種簡單查詢，也要包進 Service 方法（例：`TimeEntryService.GetCategoryOptionsAsync`），這是刻意的、對 AGENTS.md「Controller 不放 EF 查詢」規則的嚴格解讀。
3. **一律用 `CurrentUserService.GetRequiredUserId()`** 取得使用者 ID，永遠不信任前端傳來的 UserId。任何 Category/Tag/TimeEntry 的擁有權查詢都要一起帶 `UserId` 條件；找不到或「存在但不是你的」一律回同一種結果（通常是 404 + 對應 errorCode），不要用不同的回應洩漏「這個 ID 存在」這件事。
4. **`IClock`（`Infrastructure/Clock/`）取代直接呼叫 `DateTime.UtcNow`**：所有 Service 建構子注入 `IClock`，才能寫決定性（deterministic）的時間相關測試。正式環境用 `SystemClock`，測試用各專案自己的 `TestClock`（可讀寫 `UtcNow`）。
5. **併發衝突處理兩種手法，依情境選一種，不要引入資料庫專屬語法**（例如 `SELECT ... FOR UPDATE` 這種 MySQL 語法已經證實會讓测试完全無法在 SQLite/InMemory 上驗證，被我們換掉了）：
   - **樂觀式（先查再寫，衝突算失敗）**：適合「唯一索引防止重複」的情境，例如 `TimerService.StartAsync`（RunningTimer 主鍵）、`TimeEntryService.FindOrCreateTagsAsync`（Tag 的 `UNIQUE(UserId, Name)`）。寫入時包 `try/catch (DbUpdateException)`。
   - **交易 + 受影響筆數檢查**：適合「刪除/更新後才知道有沒有人搶先」的情境，例如 `TimerService.StopAsync`（要跨兩張表原子操作）。用 `Database.BeginTransactionAsync()` + 讓刪除自然對 0 筆資料丟出 `DbUpdateConcurrencyException`，捕捉後回傳「已經不存在」，讓交易隨 `await using` 自動 rollback。
6. **每個新功能一個獨立的 xUnit 測試專案**，命名慣例 `Tests/<Feature>Flow.Tests`（已有 `AuthFlow.Tests`、`TimerFlow.Tests`、`TimeEntryFlow.Tests`）。**選擇 EF Core provider 的判斷準則**：
   - 只需要基本 CRUD、不牽涉交易/唯一索引/外鍵強制執行 → 用 `Microsoft.EntityFrameworkCore.InMemory`（像 `AuthFlow.Tests`）。
   - 需要 `Database.BeginTransactionAsync()`、真正的唯一索引衝突偵測、或真正的外鍵行為（`ON DELETE SET NULL`/`CASCADE`）→ 用 SQLite shared-cache in-memory（`Data Source=file:<guid>?mode=memory&cache=shared;Default Timeout=5`，保留一個 keep-alive connection，`EnsureCreatedAsync()` 建 schema，記得先建一筆真的 `User`，因為 SQLite 會強制檢查 FK 而 InMemory provider 不會）。同時要把 `SQLitePCLRaw.bundle_e_sqlite3` 明確釘選到 `2.1.12`（預設帶入的 `2.1.11` 有已知高風險安全性公告 GHSA-2m69-gcr7-jv3q）。
   - **SQLite provider 有自己的限制要注意**：不支援對 `ulong` 欄位 `ORDER BY`（要轉型成 `long`，反正 ID 不可能大到溢位）；不支援 `FOR UPDATE` 之類的鎖 hint 語法。
7. **真正併發的情境要用 `Task.WhenAll` 寫成真的平行測試**（不是假裝的序列呼叫），斷言「恰好一個成功、資料庫最終狀態只有一份」這種不變量，而不是斷言誰先誰後（誰贏是不決定的）。範例：`TimerFlow.Tests` 的並發 Stop 測試、`TimeEntryFlow.Tests` 的並發建立同名標籤測試。本機重複跑 5 次確認沒有不穩定（flaky）。

---

## 前端慣例

- 每個主要頁面一支 `wwwroot/js/<page>.js`（ES module，`type="module"`），共用的 `callApi()` 寫法：非 GET 帶 `X-CSRF-TOKEN`（讀 `<meta name="csrf-token">`，由 `_Layout.cshtml`/`_AuthLayout.cshtml` 透過 `IAntiforgery.GetAndStoreTokens` 輸出）、`fetch` 回來 `.json().catch(() => null)`、失敗時 `throw new Error(payload?.detail ?? '發生錯誤，請稍後再試。')`。範例：`timer.js`、`time-entry.js`。
- 標籤（Tag）chip 編輯器的 UI/互動模式已經定型（輸入框 + Enter 或按鈕加入、每個 chip 有 × 移除按鈕），`timer.js` 先做出來，`time-entry.js` 的新增/編輯表單直接複用同一套模式，之後如果要做 Tag 管理頁面，也建議延續。
- **時區處理目前是刻意簡化**：所有「今天/本週/本月」這類日期範圍計算，全部用瀏覽器本地時區（`new Date()` + `.toISOString()`），因為 Settings/timezone（第 12 項）還沒做、`User.TimeZoneId` 目前對誰都是同一個寫死的預設值。**這個簡化目前套用在 Dashboard 計時器與 History List 兩處**，等第 12 項做完時需要一併重新檢視這兩處。
- Dashboard 的統計卡片/圖表（`dashboard.js` + `dashboard-state.mjs`）**刻意維持 mock data**，跟已經是真資料的計時器/History 明確切開，避免真假資料混在一起造成誤導。串接真實統計是 `TimeAggregationService`/`DashboardService`（第 9、10 項）的責任，不要在做其他功能時順手把 mock 資料跟真實資料摻在一起。

---

## 這個 session 中發現並修好的兩個重要地雷

1. **主專案 `.csproj` 的 `Content`/`None` 排除不完整，造成建置輸出資料夾指數爆炸**：原本只有 `<Compile Remove="Tests\**\*.cs" />`，只排除 C# 編譯，沒排除 Web SDK 隱含的 `Content`/`None` `**` 萬用字元規則。三個測試專案都放在主專案目錄底下、且各自 `bin/` 都含有主專案建置輸出的複本，導致主專案每建置一次就把測試專案的 `bin`（裡面又有更早一次建置的複本）當成自己的內容複製進自己的輸出，巢狀深度以指數成長（實測深到 `rm -rf` 光刪除舊資料夾就要跑好幾分鐘，`dotnet build` 慢到 4 分鐘以上）。**已修正**：`.csproj` 加上 `<Content Remove="Tests\**" />` 與 `<None Remove="Tests\**" />`。**如果你發現建置/測試莫名變慢、或看到路徑裡有 `bin\...\Tests\XxxFlow.Tests\bin\...\Tests\XxxFlow.Tests\bin\...` 這種重複巢狀，就是這個問題復發了**——檢查 `.csproj` 有沒有被改回去，並直接刪掉爆炸的 `bin`/`obj`（未被 Git 追蹤，刪除永遠安全）重建即可。
2. **只靠 SQLite/InMemory 測試不代表真的沒 bug**：`TimeEntryService.UpdateAsync` 有個真實的 `NullReferenceException`（漏了 `.ThenInclude(link => link.Tag)`），SQLite 測試因為在同一個 `DbContext` 裡先 `Create` 再 `Update`、EF 的追蹤器 identity-fixup 意外把缺漏蓋過去而完全沒抓到，直到啟動真正的開發伺服器、用 `curl` 跑一次「註冊 → 建立 → 部分更新（PATCH 不帶 tags）」的完整流程才炸出來。**教訓：寫牽涉到「跨請求讀取關聯資料」的測試時，要故意用兩個獨立的 `DbContext` 實例（一個建立、一個之後操作）模擬真實的 per-request scope，不要因為測試方便就共用同一個 `DbContext`**（`TimeEntryFlow.Tests` 裡 `UpdateAsync_omitting_tags_leaves_existing_tags_untouched` 已經改成這樣寫，可以當範本）。**如果之後要做有實質風險的新功能，建議在自動化測試都過了之後，額外啟動一次本機開發伺服器（`dotnet run --no-build`，記得先確認沒有舊的 dotnet process 佔用同個 port/鎖住 build 輸出）用 `curl` 跑一次端到端流程再收工**——這個 session 兩次都是靠這一步抓到真正的生產環境 bug。

---

## 環境操作備忘

- **本機開發用的 MySQL 連線字串與 JWT 簽章金鑰放在 `dotnet user-secrets`**（`dotnet user-secrets list` 可以看鍵名，不要印出值）。`Program.cs` 啟動時會檢查這兩個設定，缺了會直接丟例外中止啟動。
- **啟動開發伺服器**：`ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5180 dotnet run --no-build`，建議用 `nohup ... > log 2>&1 & disown` 背景執行並把輸出導到檔案，否則工具的背景任務包裝有時會在指令列結束時一併把子行程帶走。**每次要重新編譯前，記得先停掉還在跑的 dev server**（`Stop-Process -Id <pid> -Force`），否則 `Lyubishchev Time Management.exe` 會被鎖住導致 `dotnet build` 失敗（`MSB3021`/`MSB3027`）。
- **這個 repo 沒有 `.sln` 檔**，每個測試專案都是各自獨立用 `dotnet build`/`dotnet test` 在自己的資料夾裡跑，不需要（也不能）註冊進解決方案檔。
- **不要同時對同一個主專案跑多個平行的 `dotnet test`/`dotnet build`**：三個測試專案都透過 `ProjectReference` 共用同一個主專案輸出，平行跑會撞到檔案鎖，導致警告、重試、甚至看起來卡住。想一次驗證全部，請**依序**跑：`AuthFlow.Tests` → `TimerFlow.Tests` → `TimeEntryFlow.Tests`（或反過來，但不要同時）。
- Windows 上的 `tasklist`/`Get-Process` 輸出在這個殼層編碼下可能會亂碼（繁體中文訊息被解成亂碼），這是顯示問題，不影響指令實際效果。

---

## 開始接手前建議的檢查清單

1. `git log --oneline -5` 確認目前在 `74090e8` 之後（若使用者又做了其他修改，先弄清楚差異）。
2. 讀 `AGENTS.md` 全文一次（規則沒有變過，但務必自己確認，不要只信這份摘要）。
3. 讀 `Docs/TODO.md` 確認目前狀態（這份文件比本摘要新，衝突時以 `TODO.md` 為準）。
4. 依使用者這次想做的項目，讀對應的 `Docs/*.md` 設計文件（`JWT.md`／`RateLimitingAndAuthLogging.md`／`RunningTimerStartStop.md`／`TimeEntryCrud.md`）取得可重用的既有模式與已知的取捨/限制。
5. 動工前先 `dotnet build "Lyubishchev Time Management.csproj"` 一次確認目前基準是綠的，再依序跑三個測試專案確認 12/7/16 全過，作為「改動前」的基準線。
6. 做完之後：更新 `Docs/TODO.md`、視情況新增一份 `Docs/<功能名稱>.md`（比照既有四份的格式：設計思路 → 整體流程 → 檔案清單與內容 → 尚未涵蓋的部分），並在自動化測試全過之後，額外跑一次真實伺服器手動驗證再回報完成。
