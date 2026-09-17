# 專案交接摘要（給接手的 AI Agent）

最後更新：2026-09-17，涵蓋到全域例外處理與操作事件記錄完成（依照 [`Docs/superpowers/specs/2026-09-17-error-handling-and-logging-design.md`](superpowers/specs/2026-09-17-error-handling-and-logging-design.md) 與 [`Docs/superpowers/plans/2026-09-17-error-handling-and-logging.md`](superpowers/plans/2026-09-17-error-handling-and-logging.md) 實作），細節見 [`Docs/ErrorHandlingAndLogging.md`](ErrorHandlingAndLogging.md)。再往前是 RWD 切版完成（依照 [`Docs/superpowers/specs/2026-09-17-rwd-design.md`](superpowers/specs/2026-09-17-rwd-design.md) 與 [`Docs/superpowers/plans/2026-09-17-rwd.md`](superpowers/plans/2026-09-17-rwd.md) 實作），細節見 [`Docs/Rwd.md`](Rwd.md)。再往前是 CSV export 完成（依照 [`Docs/superpowers/specs/2026-09-17-csv-export-design.md`](superpowers/specs/2026-09-17-csv-export-design.md) 與 [`Docs/superpowers/plans/2026-09-17-csv-export.md`](superpowers/plans/2026-09-17-csv-export.md) 實作），細節見 [`Docs/CsvExport.md`](CsvExport.md)。再往前是 Report 完成（依照 [`Docs/superpowers/specs/2026-09-17-report-design.md`](superpowers/specs/2026-09-17-report-design.md) 與 [`Docs/superpowers/plans/2026-09-17-report.md`](superpowers/plans/2026-09-17-report.md) 實作），細節見 [`Docs/Report.md`](Report.md)。再往前是 Calendar View 完成（依照 [`Docs/superpowers/specs/2026-09-17-calendar-view-design.md`](superpowers/specs/2026-09-17-calendar-view-design.md) 與 [`Docs/superpowers/plans/2026-09-17-calendar-view.md`](superpowers/plans/2026-09-17-calendar-view.md) 實作），細節見 [`Docs/CalendarView.md`](CalendarView.md)。再往前是一次修正 DateTimeKind 序列化 bug 並把 History List 改用帳號時區（見下方「這個 session 中發現並修好的重要地雷」第 3 點），再更早是 Dashboard 真實資料串接完成（依照 [`Docs/superpowers/specs/2026-09-17-dashboard-design.md`](superpowers/specs/2026-09-17-dashboard-design.md) 與 [`Docs/superpowers/plans/2026-09-17-dashboard.md`](superpowers/plans/2026-09-17-dashboard.md) 實作）。再往前依序是 Timezone settings 與 TimeAggregationService（依照 [`Docs/superpowers/specs/2026-09-17-timezone-and-aggregation-design.md`](superpowers/specs/2026-09-17-timezone-and-aggregation-design.md) 與對應的 [`Docs/superpowers/plans/2026-09-17-timezone-settings.md`](superpowers/plans/2026-09-17-timezone-settings.md)、[`Docs/superpowers/plans/2026-09-17-time-aggregation-service.md`](superpowers/plans/2026-09-17-time-aggregation-service.md) 實作），再更早是 Category/Tag 管理功能（依照 [`Docs/superpowers/specs/2026-09-17-category-tag-management-design.md`](superpowers/specs/2026-09-17-category-tag-management-design.md) 與 [`Docs/superpowers/plans/2026-09-17-category-tag-management.md`](superpowers/plans/2026-09-17-category-tag-management.md) 實作）。本文件的目的是讓另一個 AI agent 不需要重新爬梳整個對話記錄，就能接續目前的進度。**開始工作前務必先讀 `AGENTS.md`（專案根目錄，`CLAUDE.md` 只是 `@AGENTS.md` 的轉介）——那是這個專案唯一的權威規格文件，所有設計決策都必須對齊它。**

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
| 5 | Category | ✅ 完成 | 見下方「Category/Tag 管理」章節 |
| 6 | Tag + TimeEntryTag | ✅ 完成 | 見下方「Category/Tag 管理」章節 |
| 7 | History List | ✅ 完成（隨第4項一起做，非 mock data；日期範圍/顯示/表單已改用帳號時區） | [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md) |
| 8 | Calendar View | ✅ 完成，唯讀，桌面週時間軸／手機單日時間軸，重用既有 `GET /api/time-entries`，沒有新增後端程式碼 | [`Docs/CalendarView.md`](CalendarView.md) |
| 9 | TimeAggregationService | ✅ 完成，已被 Dashboard、Report 共同消費 | [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md) |
| 10 | Dashboard | ✅ 完成，計時器卡片、統計卡片/圖表、最近活動皆為真實資料，mock data 已全部移除 | [`Docs/Dashboard.md`](Dashboard.md) |
| 11 | Report | ✅ 完成，Category 圓餅圖與 Tag 長條圖皆為真實資料，只轉接 `TimeAggregationService` 不重寫聚合邏輯 | [`Docs/Report.md`](Report.md) |
| 12 | Timezone settings | ✅ 完成（`/Settings` 頁面可選、持久化；Dashboard/History List/Calendar/Report/CSV export 皆已改用帳號時區；Timer 卡片經查證後不需要改——它的即時顯示只算經過秒數，跟日曆日期/時區無關，見下方「這個 session 中發現並修好的重要地雷」第 4 點） | [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md) |
| 13 | CSV export | ✅ 完成，`GET /api/time-entries/export` 不分頁輸出目前 History 篩選值命中的全部 TimeEntry，UTF-8 BOM RFC 4180、帳號時區顯示、時長不受 DST 影響 | [`Docs/CsvExport.md`](CsvExport.md) |
| 14 | RWD | ✅ 完成，共用 `_MobileNavigation.cshtml` + 原生 `<dialog>` More sheet，Category/Tag/Settings 首次有行動底部導覽，`dashboard.css` 統一 shell 層 44px 觸控目標/安全區/`prefers-reduced-motion`；**視覺驗證已補上**（headless Chrome CDP 截圖，320×568/375×667/768×1024/1024×768 四組視窗，沒有發現視覺缺陷） | [`Docs/Rwd.md`](Rwd.md) |
| 15 | Error handling / Logging / Rate limit | ✅ 完成，`GlobalExceptionHandler`（`IExceptionHandler`）統一處理未預期例外（`/api/*` 安全 JSON、其餘安全 HTML，皆帶 `traceId`），`IOperationalEventLogger` 記錄 Timer/CSV 非預期失敗的安全 metadata；Rate limiting 與 CSRF 沿用既有實作 | [`Docs/ErrorHandlingAndLogging.md`](ErrorHandlingAndLogging.md) |
| 16 | Nginx / EC2 / Backup | ❌ 未開始 | — |

**AGENTS.md Implementation Order 第 1–15 項已全數完成**，只剩第 16（部署）。

**建議下一步優先順序**（`Docs/TODO.md` 目前寫的）：
1. RWD 視覺驗收矩陣裡剩 200% 瀏覽器縮放、鍵盤 Tab 順序、螢幕閱讀器 focus 走向這幾項還沒測——這些是截圖驗證看不出來的，需要真人操作瀏覽器才能補
2. 第 16 項（Nginx / EC2 / Backup）部署階段尚未開始

---

## Category/Tag 管理（第 5、6 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-category-tag-management-design.md`](superpowers/specs/2026-09-17-category-tag-management-design.md) 完整實作，設計文件本身沒有 TBD，這裡只記錄實作時的具體事實：

- **唯一性鍵**：`Category`、`Tag` 都新增了持久化欄位 `NormalizedName`（`Name.Trim().ToUpperInvariant()`，由 `Infrastructure/Text/ResourceName.TryNormalize` 統一產生），取代原本的 `Category(UserId, Name)` 非唯一索引與 `Tag(UserId, Name)` 唯一索引，改為 `(UserId, NormalizedName)` 唯一索引（兩者皆是）。這樣「Focus」、`focus`、` focus `在 MySQL 與 SQLite 上都保證衝突，不依賴資料庫預設 collation。
- **Migration**：`Data/Migrations/20260917003121_AddNormalizedResourceNames.cs`。實作時發現一個 MySQL 特有的坑：不能先 `DROP INDEX` 舊索引再建新索引——`(UserId, Name)`/`(UserId, Name)` 是唯一覆蓋 `UserId` 這個外鍵欄位的索引，MySQL/InnoDB 會拒絕在沒有替代索引覆蓋外鍵欄位時把它砍掉（錯誤訊息：`Cannot drop index 'IX_Tags_UserId_Name': needed in a foreign key constraint`）。修正方式是把 `Up`/`Down` 都改成「先建新索引、回填資料、再砍舊索引」的順序（前提是新舊索引都以 `UserId` 起頭）。已在本機 MySQL dev 資料庫（`dotnet ef database update`）驗證套用成功；`Up` 內用 `UPDATE ... SET NormalizedName = UPPER(TRIM(Name))` 明確回填既有資料，而不是依賴欄位預設值。
- **API 路由與錯誤碼**：`GET/POST/PATCH{id}/DELETE{id}` 分別掛在 `/api/categories`、`/api/tags`（`CategoryApiController`/`TagApiController`，`[Authorize]` + 非 GET 皆 `[ValidateAntiForgeryToken]`）。錯誤碼：`INVALID_NAME`（400，全空白名稱）、`CATEGORY_NAME_CONFLICT`/`TAG_NAME_CONFLICT`（409，同使用者重複正規化名稱）、`CATEGORY_NOT_FOUND`/`TAG_NOT_FOUND`（404，跨使用者或不存在）。
- **刪除語意**：完全交給既有的 DB 外鍵約束處理，Service 用 `ExecuteDeleteAsync` 直接刪除，不手動處理關聯資料——Category 刪除觸發 `TimeEntries.CategoryId` 的 `ON DELETE SET NULL`；Tag 刪除觸發 `TimeEntryTags.TagId` 的 `ON DELETE CASCADE`，TimeEntry 本身都保留。
- **inline Tag 共用同一套規則**：`TimeEntryService.NormalizeTagNames`/`FindOrCreateTagsAsync` 已改為用 `ResourceName.TryNormalize` 正規化、以 `NormalizedName` 查找/建立，管理頁建立的 Tag 與 TimeEntry 編輯視窗 inline 建立的 Tag 保證是同一套唯一性判斷，不會因大小寫或空白產生第二筆。
- **前端**：`Views/Category/Index.cshtml`、`Views/Tag/Index.cshtml`（Tag 頁不含色彩欄位），`wwwroot/js/category.js`/`tag.js` 沿用 `time-entry.js` 的 `callApi()` 慣例（CSRF header、Problem Details `detail` 呈現錯誤），刪除前 `window.confirm` 提示 SET NULL / CASCADE 的後果；名稱一律用 `textContent`/DOM node 渲染，不把使用者輸入字串接進 `innerHTML`（這點比既有 `time-entry.js` 的 `renderEntryRow` 更嚴謹，後者仍是字串拼接，如果之後要動 History List 建議一併檢視）。Dashboard、History List 側欄的「分類」「標籤」`href="#"` 已改為指向這兩個真實路由；「報表」「設定」仍維持 placeholder。
- **測試**：新增 `Tests/TimeEntryFlow.Tests/TestDatabase.cs`（抽出共用的 SQLite shared in-memory fixture，`TimeEntryServiceTests.cs` 也改用它），`Integration/CategoryServiceTests.cs`、`Integration/TagServiceTests.cs`，以及 `TimeEntryServiceTests` 新增一個 inline tag 正規化重用的回歸測試。`Tests/TimeEntryFlow.Tests` 目前共 35 個測試全過；併發重複建立 Tag 的測試（`Task.WhenAll` 真平行）額外重複執行 4 次確認無 flaky。
- **手動驗證**：本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 走完整流程：註冊 → 進入 `/Category`、`/Tag` 頁面（200，DOM 結構正確、Tag 頁確認不含色彩欄位）→ 建立/改名/刪除 Category（含指派給一筆 TimeEntry 後刪除，確認該筆變成未分類）→ 重複名稱回 409 → 建立 Tag、以 inline 方式在 TimeEntry 建立時重用同一個正規化 Tag、刪除 Tag 後確認 TimeEntry 保留但標籤消失。驗證用的測試資料（TimeEntry）已於驗證後刪除。
- **範圍邊界**：完全比照設計文件「不在範圍」——沒有動到 Dashboard/Report 彙總、時區設定、CSV 匯出；`TimeEntryService` 只改了 inline tag 正規化這一段，其餘 CRUD 邏輯未變動。

---

## Timezone settings 與 TimeAggregationService（第 9、12 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-timezone-and-aggregation-design.md`](superpowers/specs/2026-09-17-timezone-and-aggregation-design.md) 完整實作，設計文件本身沒有 TBD，完整細節（設計思路、整體流程、檔案清單、手動驗證紀錄）見獨立文件 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)，這裡只記重點：

- **`TimeZoneCatalog`（`Infrastructure/Time/`）是唯一的時區白名單/顯示名稱/`TimeZoneInfo` 解析入口**，`UserSettingsService`（寫入 `User.TimeZoneId`）與 `TimeAggregationService`（讀取範圍）共用同一份；13 個常用 IANA 時區，不在白名單的 ID 一律降級為 `UTC`（只保護舊資料，正常更新流程一定先驗證過）。
- **`TimeAggregationService` 是唯讀服務**：EF 只做 `UserId` 擁有權與 `StartTimeUtc < rangeEndUtc && EndTimeUtc > rangeStartUtc` 的 overlap 過濾，時區轉換／DST 邊界／逐日切分／Category/Tag 分桶全部在記憶體完成（`TimeZoneInfo` 無法翻譯進 EF query）。DST 處理規則：spring-forward 缺口的本地午夜往前找第一個有效時間；fall-back 歧義的本地午夜固定選較早的 UTC 瞬間，確保連續日期範圍前後不重疊、不留縫。
- **重要地雷：`Dictionary<ulong?, TValue>` 執行期不接受 `null` key，即使 `TKey` 是 nullable 值型別。** 第一版想用 `null` key 代表「未分類」Category 桶，編譯器丟出 `CS8714` 時一開始誤判是型別系統過度嚴格而直接 `#pragma warning disable` 蓋掉，結果測試一跑就在 `Dictionary.FindValue` 炸出 `ArgumentNullException`——`Dictionary` 執行期禁止 `null` key 跟 `TKey` 是不是 nullable 值型別無關。**教訓：`CS8714` 這類警告不要當雜訊蓋掉，先假設它在指出真的執行期問題。** 修正方式：未分類桶改用獨立的 `long` 累加器，不塞進 `Dictionary` 的 key。細節見 `Docs/TimezoneAndAggregation.md` 的「開發過程中抓到的問題」。
- **API 路由與錯誤碼**：`GET`/`PATCH /api/settings/timezone`（`SettingsApiController`，`[Authorize]`，PATCH 額外 `[ValidateAntiForgeryToken]`）。錯誤碼：`INVALID_TIME_ZONE`（400，缺少/空白/不支援的 ID 都算，刻意不用 ModelState annotation 驗證，避免跟 service 驗證分裂成兩種錯誤格式）、`USER_NOT_FOUND`（404）。
- **測試**：新增 `Tests/TimeEntryFlow.Tests/Unit/TimeZoneCatalogTests.cs`（純單元測試，仿 `AuthFlow.Tests` 的 `Unit`/`Integration` 分法）、`Integration/UserSettingsServiceTests.cs`、`Integration/TimeAggregationServiceTests.cs`（沿用既有 `TestDatabase` SQLite fixture）。`Tests/TimeEntryFlow.Tests` 目前共 70 個測試全過。
- **手動驗證**：本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 走完整流程：註冊 → `GET /Settings`（200）→ `GET /api/settings/timezone`（預設 `Asia/Taipei`）→ `PATCH` 改成 `America/New_York`（200）→ 再次 `GET` 確認持久化 → `PATCH` 不支援 ID（400 `INVALID_TIME_ZONE`）。驗證用的測試帳號已於驗證後刪除。
- **範圍邊界**：完全比照設計文件「不在範圍」——沒有動到 Dashboard/Report 真實資料 API、Calendar View、CSV export、既有 TimeEntry CRUD 表單，也沒有把其他頁面側欄的「設定」`href="#"` 改指向新的 `/Settings` 路由（刻意把改動鎖在設計文件列出的檔案清單內）。

---

## Dashboard（第 10 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-dashboard-design.md`](superpowers/specs/2026-09-17-dashboard-design.md) 完整實作，設計文件本身沒有 TBD，完整細節見獨立文件 [`Docs/Dashboard.md`](Dashboard.md)，這裡只記重點：

- **`DashboardService` 只組裝，不重寫任何聚合邏輯**：本期與比較期都呼叫既有的 `TimeAggregationService`（`GetRangeForPreset`/`AggregateAsync`），最近五筆活動是獨立、不受選定範圍限制的查詢。Report（第 11 項）串接時應延續這個模式，不要在 `ReportService` 裡另外實作交集/DST/分桶邏輯。
- **比較期演算法依模式分兩種**：Today/Week/Custom 用「相鄰、等長」（day count 相同），Month 用「前一個曆月」而非等長天數——因為月份長度不固定（9 月 30 天對 8 月 31 天），使用者比較的是行事曆月份。這是唯一能同時滿足設計文件「相鄰且等長」與「ThisMonth 對前一個曆月」兩種措辭的解讀，已用不同長度月份的組合寫測試驗證。
- **preset 與 custom range 互斥**：`GET /api/dashboard` 只接受「恰好一種」合法範圍指定方式；兩者都給、都不給、preset 未知、custom 只給一半，一律 400 `INVALID_RANGE_PRESET`；custom 合法但 `endDate < startDate` 才是 400 `INVALID_DATE_RANGE`。
- **前端 mock data 全部移除**：`dashboard-state.mjs` 從假資料模組改造成兩個純函式（`formatDuration`——現在吃秒數不是分鐘、`isValidDateRange`）的共用工具模組，繼續保留這個檔案（沒有整個刪掉）是因為它是唯一不依賴 `document` 的模組，`Tests/Unit/dashboard-frontend.test.mjs` 才能在 Node 環境下測試純邏輯。`dashboard.js` 改成對 `/api/dashboard` 發真實 fetch，用遞增 `requestId` 忽略較舊的回應；Timer 卡片完全沒有被觸碰，仍由 `timer.js` 獨立運作。
- **最近活動的時間顯示已經改用帳號時區**（`Intl.DateTimeFormat` + 回應裡的 `timeZoneId`），是目前整個 Dashboard 唯一已經套用帳號時區顯示時間的地方；Timer 卡片即時顯示與 History List 的日期範圍篩選都還是舊的瀏覽器本地時區簡化，尚未改（見下方「建議下一步優先順序」第 2 項）。
- **順手修好一個既有的測試錯誤**：`Tests/Unit/dashboard-frontend.test.mjs` 原本最後一個測試對共用版面 `_Layout.cshtml` 斷言含有 `dashboard.css`/`dashboard.js` 字樣，但這兩個資源其實是 `Views/Dashboard/Index.cshtml` 用 `@section Styles`/`@section Scripts` 載入的，共用版面從未直接出現這兩個檔名——這個斷言在改動前就是錯的（用 `node --test` 實際跑過原始檔驗證會失敗），不是這次改動造成的迴歸。已改成分別讀取兩個檔案各自斷言正確的內容。
- **測試**：新增 `Tests/TimeEntryFlow.Tests/Integration/DashboardServiceTests.cs`（沿用 `TestDatabase` SQLite fixture），涵蓋 Today/Week/Month/Custom 比較期（含不同長度月份）、無效 preset/date range、preset 與 custom 同給、空資料、最近五筆排序、跨使用者隔離、Category/Tag totals 透傳。`Tests/TimeEntryFlow.Tests` 目前共 83 個測試全過；`dashboard-frontend.test.mjs` 用 `node --test` 執行，4 個測試全過。
- **手動驗證**：本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 建立 Category、一筆跨 UTC 午夜但落在同一個台北本地日的 TimeEntry，走過 `GET /api/dashboard` 的 today/week/month/custom/各種錯誤組合，並確認 `GET /Dashboard` 頁面正確接線 `dashboard.js`/`dashboard.css`/「查看全部」連結。這個 session 的環境沒有可用的瀏覽器自動化工具（`claude-in-chrome` 技能名稱有列出但實際呼叫時回報未知技能，`WebFetch` 明確不支援 localhost），因此桌面/320px 版面與實際點擊互動**沒有**在真的瀏覽器裡驗證過，接手後建議補這一步。驗證用的測試資料（Category、TimeEntry、帳號）已於驗證後刪除。

---

## Calendar View（第 8 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-calendar-view-design.md`](superpowers/specs/2026-09-17-calendar-view-design.md) 與 [`Docs/superpowers/plans/2026-09-17-calendar-view.md`](superpowers/plans/2026-09-17-calendar-view.md) 實作，完整細節見獨立文件 [`Docs/CalendarView.md`](CalendarView.md)，這裡只記重點：

- **沒有新增任何後端程式碼**：計畫的 Task 1（保護可視範圍資料讀取）檢查後發現 `TimeEntryService.ListAsync`/`TimeEntryApiController.List` 早就滿足所有條件（overlap 查詢、`UserId` 擁有權、`AsNoTracking`、`pageSize` clamp 在 `[1,200]`），且既有測試已覆蓋邊界情境。Calendar 純粹是 `/TimeEntry` History 頁的第二種前端檢視，跟 List 共用同一支 `GET /api/time-entries`。
- **V1 完全唯讀**：不發 POST/PATCH/DELETE，不整合 List 的 Category/搜尋篩選（規格沒要求），切到 Calendar 時會隱藏 List 的 filter-bar 與分頁，只留自己的導覽（上一段／今天／下一段）。
- **桌面週視圖／手機單日視圖靠 `window.matchMedia('(min-width: 768px)')` 判斷**，跨越斷點會重新 fetch（因為可視的日期範圍本身不同，不只是 CSS 換版型）。
- **切段與分欄邏輯抽成純函式模組** `wwwroot/js/calendar-state.mjs`（`splitEntryIntoDaySegments`/`layoutOverlappingSegments`，無 DOM/Intl 依賴），比照 `dashboard-state.mjs` 的模式方便 Node 測試；`wwwroot/js/timezone.mjs` 新增 `getZonedDateParts`/`addZonedDays`/`weekdayOfDate`/`zonedDayUtcBounds` 供 `calendar.js` 做週/日邊界的時區換算，既有匯出不動。
- **測試**：`Tests/Unit/calendar-state.test.mjs`（5 個）、`Tests/Unit/timezone.test.mjs` 新增 4 個，`Tests/Unit/*.test.mjs` 目前共 23 個 Node 測試全過；C# 測試沒有新增（沒有新後端邏輯），`TimeEntryFlow.Tests` 83 個測試重跑確認沒有迴歸。
- **手動驗證**：本機啟動 `dotnet run --no-build`，用 `curl` 確認 `/TimeEntry` 頁面含所有新 DOM id、`calendar.css`/`calendar.js`/`calendar-state.mjs` 皆可靜態存取，建立同日不重疊、同日重疊、跨本地午夜三種 TimeEntry，用 `calendar.js` 實際會算出的本週 UTC 範圍打 `GET /api/time-entries` 確認三筆都正確回傳。**這個 session 一樣沒有可用的瀏覽器自動化工具**（跟 Dashboard 那次相同狀況），所以週欄/單日版面的實際視覺效果、focus 順序、320px 版型都沒有在真的瀏覽器裡驗證過——這是接手後最優先該補的一步。驗證用的測試資料與帳號已刪除。

---

## Report（第 11 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-report-design.md`](superpowers/specs/2026-09-17-report-design.md) 與 [`Docs/superpowers/plans/2026-09-17-report.md`](superpowers/plans/2026-09-17-report.md) 實作，完整細節見獨立文件 [`Docs/Report.md`](Report.md)，這裡只記重點：

- **`ReportService` 跟 `DashboardService` 一樣只組裝、不重寫任何聚合邏輯**：`GetCategoryAsync`/`GetTagAsync` 都只呼叫 `TimeAggregationService.GetRangeForPreset`/`AggregateAsync`，取回傳結果裡的 `CategoryTotals`/`TagTotals` 各自那一半組成回應，沒有第二份 interval intersection/時區轉換/分桶程式碼。
- **跟 Dashboard 唯一的行為差異是預設值**：`GET /api/dashboard` 在什麼都不給時回 400 `INVALID_RANGE_PRESET`；`GET /api/reports/category`/`GET /api/reports/tag` 依規格書「預設 month」，什麼都不給時直接當 `preset=month` 處理。除此之外 preset/custom 互斥驗證、`INVALID_DATE_RANGE`/`INVALID_RANGE_PRESET` 判斷完全比照 `DashboardService`。
- **前端刻意讓 Category 與 Tag 走不同的百分比規則**：Category legend 顯示「時長（百分比）」，分母是 `TotalSeconds`；Tag bar 只顯示時長、寬度只相對於當期最大值，**不顯示百分比**——因為 Tag 是可加總的（一筆多 Tag 紀錄的全時長算進每個 Tag），總和可能超過 `TotalSeconds`，顯示百分比會誤導使用者。這是 `AGENTS.md`「不用圓餅圖表示 Tag」規則的前端落實。
- **沒有新增圖表函式庫**：重用 `dashboard.css` 既有的 `.donut-layout`/`.category-donut`/`.chart-legend`/`.tag-chart`/`.tag-row`（conic-gradient 圓餅、flex 長條），`report.css` 只新增 `.report-grid` 的桌面雙欄／900px 以下單欄堆疊（斷點對齊 `dashboard.css` 的側欄收合斷點，避免 768–900px 之間版面不協調）。
- **側欄「報表」`href="#"` 已全站改指向 `/Report`**：`Views/Dashboard`、`TimeEntry`、`Category`、`Tag`、`Settings` 的 `Index.cshtml`（含 Dashboard 的 `mobile-nav`）都已更新，這是延續上一輪 bug fix（Settings 連結）時發現的同一種 placeholder 問題。
- **測試**：新增 `Tests/TimeEntryFlow.Tests/Integration/ReportServiceTests.cs`（14 個測試，沿用 `TestDatabase` SQLite fixture），涵蓋預設本月、三種 preset、custom range（含半給視為 `INVALID_RANGE_PRESET`）、preset 與 custom 同給、無效 preset/date range、未分類色彩、Tag additive 加總與排序、空資料、跨使用者隔離、Category 依時長排序。`Tests/TimeEntryFlow.Tests` 目前共 97 個測試全過。
- **手動驗證**：本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 走完整流程：註冊 → 建立 2 個 Category、3 筆 TimeEntry（含未分類、跨 Category 多 Tag 疊加）→ `GET /Report`（200，`report.js`/`report.css` 正確接線）→ 不給參數確認預設本月且未分類/Tag 疊加加總正確 → 各 preset → custom range → 各種錯誤組合（無效 preset、無效日期範圍、preset 與 custom 同給）全部回正確的 400/errorCode → 確認 Dashboard/History/Category/Tag/Settings 側欄與行動導覽的「報表」連結都已指向 `/Report`。這個 session 同樣沒有可用的瀏覽器自動化工具，桌面雙欄／900px 以下單欄堆疊的實際視覺效果沒有在真的瀏覽器裡驗證過。驗證用的測試資料（Category、TimeEntry、Tag、帳號）已於驗證後刪除。

---

## CSV Export（第 13 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-csv-export-design.md`](superpowers/specs/2026-09-17-csv-export-design.md) 與 [`Docs/superpowers/plans/2026-09-17-csv-export.md`](superpowers/plans/2026-09-17-csv-export.md) 實作，完整細節見獨立文件 [`Docs/CsvExport.md`](CsvExport.md)，這裡只記重點：

- **明細匯出，不是彙總報表**：`CsvExportService` 刻意不呼叫 `TimeAggregationService`——那個服務會把 TimeEntry 依查詢邊界裁切成統計區間交集，匯出要的是每一筆的原始 `StartTimeUtc`/`EndTimeUtc`/時長，裁切會讓明細失真。查詢邏輯改成跟 `TimeEntryService.ListAsync` 同構但完全獨立（同樣的 overlap/擁有權/篩選/排序），唯一差異是不做 `Skip`/`Take`，匯出無分頁上限。
- **修正計畫範例程式碼裡一個真的 BOM bug**：`CsvWriter.Write` 原始草稿直接用 `Utf8WithBom.GetBytes(text)`，但 `Encoding.GetBytes()` 不論 `encoderShouldEmitUTF8Identifier` 設定為何都不會自動加 BOM（那個旗標只影響 `GetPreamble()`）。照計畫先寫的 `CsvWriterTests` 一跑全部卡在 BOM 斷言，改成手動 `GetPreamble()` + `GetBytes()` 拼接才過。**教訓：計畫文件裡附的範例程式碼不是自動正確的，一樣要照著先寫失敗測試再實作的流程走一遍。**
- **`Category` 欄位的「未分類」值故意是英文字面量 `Uncategorized`**，跟 App 介面（Dashboard/`TimeAggregationService`）用的中文「未分類」不同調——這是設計文件明講的既定格式，不是疏漏。
- **History List 的帳號時區換算在前一輪（[Docs/HANDOFF.md 第 3 點重要地雷](#這個-session-中發現並修好的兩個重要地雷)）就已經修好**，這次不需要再補設計文件裡提到的「prerequisite adjustment」。
- **測試**：新增 `Tests/TimeEntryFlow.Tests/Unit/CsvWriterTests.cs`（3 個：BOM/CRLF/escape/公式中和、空資料只剩 header）、`Tests/TimeEntryFlow.Tests/Integration/CsvExportServiceTests.cs`（5 個：完整排序＋時區＋Uncategorized＋Tag 排序、跨午夜範圍內完整一筆、201 筆無分頁上限、跨使用者隔離＋空結果、New York 春季 DST 時長用 UTC 差值不是牆上鐘面相減）。`Tests/TimeEntryFlow.Tests` 目前共 105 個測試全過。
- **手動驗證**：本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 走完整流程：註冊 → 建立 Category、一筆有分類雙 Tag 的 TimeEntry、一筆跨本地午夜的未分類 TimeEntry → 不帶範圍匯出（`200`、`Content-Disposition: attachment; filename=time-entries-all.csv`、位元組開頭 `EF BB BF`、兩筆依 `StartTimeUtc` 降冪、欄位含 `Alpha; zeta` 排序與正確時長）→ 帶今天範圍匯出（檔名變成 `time-entries-20260917-20260917.csv`）→ 帶 `categoryId` 只回那一筆 → `endUtc <= startUtc` 回 `400 INVALID_TIME_RANGE` → 未登入回 `401` → 確認 `/TimeEntry` 頁面有「匯出 CSV」按鈕。驗證用的測試資料（TimeEntry、Category、帳號）已於驗證後刪除。

---

## RWD（第 14 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-rwd-design.md`](superpowers/specs/2026-09-17-rwd-design.md) 與 [`Docs/superpowers/plans/2026-09-17-rwd.md`](superpowers/plans/2026-09-17-rwd.md) 實作，完整細節見獨立文件 [`Docs/Rwd.md`](Rwd.md)，這裡只記重點：

- **不是新功能，是把既有六個頁面各自重複的側欄/行動 header/底部導覽標記收斂成一份共用局部檢視**：`Views/Shared/_MobileNavigation.cshtml`（`@model string` 決定 active 狀態）+ `wwwroot/js/mobile-navigation.mjs`/`.js`。三個版面帶（桌面 `>=901px`、平板/窄桌面 `721–900px`、手機 `<=720px`）完全沿用 `dashboard.css` 既有的 900px/720px/430px 斷點，沒有新增第四種導覽模式。
- **Category、Tag、Settings 先前在手機上完全沒有底部導覽（死路）**，這次補齊；底部導覽固定五項（Dashboard/歷史紀錄/報表/設定/更多），Category、Tag 收進原生 `<dialog>` 實作的「更多」底部彈出選單，不佔用第六個固定項目、也不需要第三方選單/對話框套件。
- **`createMoreSheetController` 把三種關閉路徑（關閉鈕、backdrop 點擊、瀏覽器原生 Escape）收斂成同一段 `dialog` 的 `close` 事件處理**，只在那裡同步 `aria-expanded` 跟把 focus 還給觸發按鈕，不用在三個地方各寫一次。
- **順手修好一個既有 bug**：`Views/Settings/Index.cshtml` 從來沒有載入 `category-tag.css`，但「儲存」按鈕的 class 是 `primary-button`，這個 class 唯一的定義過去只在 `category-tag.css` 裡——代表 Settings 的儲存按鈕一直是瀏覽器預設樣式，完全沒套用設計。修法是把 `.primary-button` 移到每頁都會載入的 `dashboard.css`，當成跟 `.outline-button` 同等級的共用元件。
- **順手修好一個 `git diff --check` 的假警訊**：`_Layout.cshtml` 是全專案唯一一個 CRLF 檔案（其餘 `.cshtml`/`.css`/`.js` 全部是 LF），插入新的一行後，git 預設的 `core.whitespace`（沒開 `cr-at-eol`）會把每一行結尾的 `\r` 當成 trailing whitespace，但只有「新增的那一行」會被 `git diff --check` 檢查到（既有的 context 行不會）。**這不是我的新行有問題，是這個檔案本來的換行慣例跟全專案不一致**——已把整個檔案統一成 LF，跟其餘檔案一致，順便讓這個假警訊消失。
- **Calendar 的 768px matchMedia 斷點刻意沒有跟著「一律改用 720px」**：`calendar.js` 用 `window.matchMedia('(min-width: 768px)')` 決定要抓「一週」還是「一天」的資料，這是資料抓取邏輯，不是純視覺斷點；如果改成跟 RWD 的 720px 手機帶對齊，會讓 721–767px 這段視覺上看起來像手機（單欄）但 JS 還在抓一整週的資料，兩者不一致。CSS 裡新增的 `.calendar-nav{width:100%}` 等規則沿用原本的 768px 是刻意的。
- **測試**：新增 `Tests/Unit/mobile-navigation.test.mjs`（5 個測試，`FakeElement`/`FakeDialog` 假元件，`FakeDialog.close()` 會像真的 `<dialog>` 一樣觸發 `close` 事件），涵蓋 `isMoreSection` 分類、開啟時 `aria-expanded`/focus 同步、backdrop 關閉＋焦點還原、面板內點擊不關閉、關閉鈕。`Tests/Unit/` 目前共 27 個 Node 測試全過；三個 C# 測試專案（`AuthFlow.Tests`/`TimerFlow.Tests`/`TimeEntryFlow.Tests`，105 個）重跑確認沒有迴歸（RWD 沒有動到任何後端邏輯）。`git diff --check` 確認乾淨。
- **手動驗證（結構面）**：本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 走完整流程：註冊 → 六個受保護頁面各自 `GET` 200、恰好一個 `#more-sheet`、五個底部導覽控制項、active 狀態正確對應目前路由（含 More 按鈕在 Category/Tag 頁面上顯示 active）、品牌連結不再是 `href="#"`、`icon-button` markup 完全移除、More sheet 內的 Category/Tag/登出連結正確、桌面側欄 active 狀態正確 → 確認 `dashboard.css`/`report.css`/`history.css`/`calendar.css`/`category-tag.css`/`settings.css`/`auth.css`/`mobile-navigation.js`/`.mjs` 皆可靜態存取（200）→ Login/Register 頁面 200、`auth.css` 含新增的 479px 規則。驗證用的測試帳號已用 `mysql` CLI（`dotnet user-secrets list` 取得本機開發連線資訊）直接刪除。
- **視覺驗證（後續 session 補上）**：這台機器有 Chrome 但沒裝 Playwright/Puppeteer，改用 Node 24 內建的 `WebSocket` 直接對 `chrome.exe --headless=new --remote-debugging-port` 講 Chrome DevTools Protocol（`Page.navigate`/`Emulation.setDeviceMetricsOverride`/`Page.captureScreenshot`），不用裝任何 npm 套件。對六個頁面在 320×568／375×667／768×1024／1024×768 四組視窗尺寸下截圖，額外開 More sheet、新增紀錄對話框、Calendar 週視圖，資料裡刻意放了長分類名稱與長標籤名稱驗證換行。**逐張截圖檢視後沒有發現任何視覺缺陷**：320px 四頁皆無水平溢出、長分類名稱正確在卡片內換行；375px More sheet 底部彈出樣式正確、對話框儲存/取消按鈕在底部導覽上方清楚可見；768px Report 收成單欄（Category 在上 Tag 在下）、Calendar 週視圖橫向排列正常；1024px 桌面側欄六連結＋登出正確顯示、Report 雙欄且 legend 百分比與時長正確配對。過程中兩個值得記的插曲，寫進 [`Docs/Rwd.md`](Rwd.md) 的「視覺驗證」一節：(1) 驗證期間並行 session 的 build/restart 循環把共享的 dev server 砍了好幾次，讓驗證腳本自己學會偵測健康狀態、必要時自行 `spawn` 一份 `dotnet run --no-build`，也用 `SendMessage` 跟對方協調暫停幾分鐘；(2) 驗證腳本一開始重用了登入前頁面的 CSRF token 去打登入後的 API，每次都吃一個空 body 的 `400`——ASP.NET Core 的 antiforgery token 會把「當下是否已驗證身分」編進去，登入前產生的 token 在登入後失效是預期行為，不是產品 bug，改成登入成功後先導到 `/Dashboard` 重新拿 token 就正常了。驗證用的測試資料（5 個測試帳號，含中途失敗留下的）已用 `mysql` CLI 依 FK 順序清乾淨。

---

## Error Handling and Logging（第 15 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-error-handling-and-logging-design.md`](superpowers/specs/2026-09-17-error-handling-and-logging-design.md) 與 [`Docs/superpowers/plans/2026-09-17-error-handling-and-logging.md`](superpowers/plans/2026-09-17-error-handling-and-logging.md) 實作，完整細節見獨立文件 [`Docs/ErrorHandlingAndLogging.md`](ErrorHandlingAndLogging.md)，這裡只記重點：

- **既有的 400/401/404/409/429 Result/ErrorCode 對應完全沒動**：這次只補「非預期」例外——一個全域 `Infrastructure/Errors/GlobalExceptionHandler.cs`（`IExceptionHandler`）攔截，`/api/*` 回安全 RFC 7807 JSON（`title=INTERNAL_SERVER_ERROR`、`detail` 用 `AGENTS.md` 規定的「發生錯誤，請稍後再試。」）、其餘路徑安全渲染 `/Home/Error`，兩者共用同一個 `traceId` 可以在 Console/journal 對應到完整例外紀錄。所有環境行為一致（不再像舊碼那樣只在非 Development 才掛例外處理），開發時的除錯完全靠 Console 的完整堆疊，不透過回應內容洩漏。
- **抓到一個沒人在文件裡提到的 framework 行為**：`ExceptionHandlerOptions.ExceptionHandlingPath` 會在呼叫任何自訂 `IExceptionHandler` *之前*，就先把 `HttpContext.Request.Path` 覆寫成設定的路徑（`/Home/Error`）——第一版直接讀 `httpContext.Request.Path.StartsWithSegments("/api")` 判斷分流，結果不管原始請求是不是 `/api/*` 都讀到已經被改寫的值，導致 API 例外也被誤判成 HTML fallback。寫了一支臨時的 `CapturingLoggerProvider` 把 handler 內部狀態記下來才抓到；正確作法是改讀 `httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path`（這個 feature 在改寫 `Request.Path` 前就已經設好，內容是真正的原始路徑）。**教訓：`ExceptionHandlerOptions.ExceptionHandlingPath` 不只是設定值，它會改變 `HttpContext` 本身的可觀察狀態，自訂 `IExceptionHandler` 不能信任 `Request.Path` 反映原始請求。**
- **第二個地雷**：預設 Console formatter 就算開了 `IncludeScopes`，也不會把 `Dictionary<string,object?>` 形式的 scope state 渲染成 key=value（只印型別名稱）。改用 message-template scope（`logger.BeginScope("TraceId:{TraceId} RequestMethod:{RequestMethod} RequestPath:{RequestPath}", ...)`）才正確顯示；`IncludeScopes` 本身也是在 `Program.cs` 用 `builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true)` 才生效，單純寫在 `appsettings.json` 的 `Logging:Console:FormatterOptions:IncludeScopes` 沒有作用（原因未深究，兩者都留著，程式碼內顯式設定是實際生效的那個）。
- **`Infrastructure/Logging/IOperationalEventLogger`／`OperationalEventLogger`**：`TimerTransactionFailed(userId, operation, exception)`（Event ID 1001）、`CsvExportFailed(userId, hasRange, hasCategoryFilter, hasSearch, exception)`（Event ID 1002），參數全是安全的基本型別，呼叫端不會意外塞進使用者輸入。`TimerService.StopAsync` 的交易改成 `IDbContextTransaction? transaction = null` + try/catch(非 `DbUpdateConcurrencyException`)/finally 結構，非預期失敗先記錄再 rollback 再重新拋出；`CsvExportService.ExportAsync` 同樣包一層（排除 `OperationCanceledException`）。兩個既有 Service 的建構子都多了一個 `IOperationalEventLogger` 參數，`TimerFlow.Tests`/`TimeEntryFlow.Tests` 裡建構這兩個 Service 的地方都要多傳一個（可以直接用 `new OperationalEventLogger(NullLogger<OperationalEventLogger>.Instance)` 當 no-op）。
- **新測試專案 `Tests/WebFlow.Tests`**：第一個用 `WebApplicationFactory<Program>` + SQLite（同其他專案的 shared-cache in-memory 手法）host 真正應用程式的測試專案。`Program.cs` 因此新增一個 `public partial class Program;` 標記（top-level statements 預設產生的 `Program` 類別要顯式標成 `public` 才能被 `WebApplicationFactory<Program>` 看到），以及一個 `Testing` 環境專用分支：略過 MySQL 的 `AddDbContext`（讓測試專案能乾淨換成 SQLite，不會因為兩邊都呼叫 `AddDbContext<AppDbContext>` 而互相覆蓋——`WebApplicationFactory` 的 `ConfigureServices` 覆寫其實跑在 `Program.cs` 自己的程式碼*之前*，這點也是這次才確認的）、以及只在這個環境才會 map 的 `/api/test/throw`、`/test/throw` 測試端點。`CustomWebApplicationFactory` 用環境變數（`ConnectionStrings__DefaultConnection`/`Jwt__SigningKey` 等）餵假設定給 `Program.cs` 的啟動守衛，因為那些檢查只認環境變數/組態，不認 `WebApplicationFactory` 的 `ConfigureAppConfiguration`（一樣是組合順序問題）。認證流程的測試要注意：`AccountController` 的 auth cookie 標記 `Secure`，`WebApplicationFactory.CreateClient()` 預設的 `http://localhost` base address 會讓 cookie 容器直接丟掉它，要改用 `new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }`（TestServer 不會真的檢查 TLS，這只影響 `HttpClient` 的 cookie 容器判斷）。速率限制測試（連打 11 次壞密碼登入）用了獨立的 factory 實例，避免跟其他測試共用同一個 IP 分區配額。共 9 個測試，四個 C# 測試專案合計 136 個測試全過。
- **手動驗證**：`ASPNETCORE_ENVIRONMENT=Testing`（`--no-launch-profile` 略過 `launchSettings.json`，`Jwt__SigningKey` 用環境變數帶假值，因為 user-secrets 只在 Development 載入）跑真正的 Kestrel，打兩個測試端點確認回應安全、`traceId` 跟 Console log 的 scope 完全對得上；再切到 `ASPNETCORE_ENVIRONMENT=Development` 接真正的 MySQL，確認已知業務錯誤（404/400）、正常 Start/Stop Timer、Register 的 auth log 都沒有被這次改動影響（400/404 的回應現在多了一個免費附贈的 `traceId`，是 `AddProblemDetails()` 全站生效的效果，不是特地加的）。驗證用的測試資料已刪除。

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
- **時區處理仍未完全收尾，但已有進展**：Settings/timezone（第 12 項）、Dashboard（第 10 項）、History List、Calendar View、Report（第 11 項）都已完成，都正確使用 `TimeAggregationService`/帳號時區。**只剩 Timer 卡片（`timer.js`）的即時顯示仍是用瀏覽器本地時區（`new Date()` + `.toISOString()`），還沒有改成讀取使用者設定的時區**——留給後續專門處理（見「建議下一步優先順序」第 1 項）。
- Dashboard 的統計卡片/圖表（`dashboard.js` + `dashboard-state.mjs`）**mock data 已全部移除**，改為對 `/api/dashboard` 發真實 fetch；`dashboard-state.mjs` 現在只保留 `formatDuration`/`isValidDateRange` 兩個純函式，細節見 [`Docs/Dashboard.md`](Dashboard.md)。
- **行動裝置導覽已經統一**：六個受保護頁面都用 `<partial name="_MobileNavigation" model="@("dashboard"|"time-entry"|"report"|"category"|"tag"|"settings")" />`，不要再手刻 `<nav class="mobile-nav">`；新增頁面時比照這個模式加一行 partial 呼叫即可自動拿到五項底部導覽跟「更多」選單，不用重複任何標記。`mobile-navigation.js` 已經在 `_Layout.cshtml` 全站載入一次，不需要每頁各自 `<script>` 引入。

---

## 這個 session 中發現並修好的重要地雷

1. **主專案 `.csproj` 的 `Content`/`None` 排除不完整，造成建置輸出資料夾指數爆炸**：原本只有 `<Compile Remove="Tests\**\*.cs" />`，只排除 C# 編譯，沒排除 Web SDK 隱含的 `Content`/`None` `**` 萬用字元規則。三個測試專案都放在主專案目錄底下、且各自 `bin/` 都含有主專案建置輸出的複本，導致主專案每建置一次就把測試專案的 `bin`（裡面又有更早一次建置的複本）當成自己的內容複製進自己的輸出，巢狀深度以指數成長（實測深到 `rm -rf` 光刪除舊資料夾就要跑好幾分鐘，`dotnet build` 慢到 4 分鐘以上）。**已修正**：`.csproj` 加上 `<Content Remove="Tests\**" />` 與 `<None Remove="Tests\**" />`。**如果你發現建置/測試莫名變慢、或看到路徑裡有 `bin\...\Tests\XxxFlow.Tests\bin\...\Tests\XxxFlow.Tests\bin\...` 這種重複巢狀，就是這個問題復發了**——檢查 `.csproj` 有沒有被改回去，並直接刪掉爆炸的 `bin`/`obj`（未被 Git 追蹤，刪除永遠安全）重建即可。
2. **只靠 SQLite/InMemory 測試不代表真的沒 bug**：`TimeEntryService.UpdateAsync` 有個真實的 `NullReferenceException`（漏了 `.ThenInclude(link => link.Tag)`），SQLite 測試因為在同一個 `DbContext` 裡先 `Create` 再 `Update`、EF 的追蹤器 identity-fixup 意外把缺漏蓋過去而完全沒抓到，直到啟動真正的開發伺服器、用 `curl` 跑一次「註冊 → 建立 → 部分更新（PATCH 不帶 tags）」的完整流程才炸出來。**教訓：寫牽涉到「跨請求讀取關聯資料」的測試時，要故意用兩個獨立的 `DbContext` 實例（一個建立、一個之後操作）模擬真實的 per-request scope，不要因為測試方便就共用同一個 `DbContext`**（`TimeEntryFlow.Tests` 裡 `UpdateAsync_omitting_tags_leaves_existing_tags_untouched` 已經改成這樣寫，可以當範本）。**如果之後要做有實質風險的新功能，建議在自動化測試都過了之後，額外啟動一次本機開發伺服器（`dotnet run --no-build`，記得先確認沒有舊的 dotnet process 佔用同個 port/鎖住 build 輸出）用 `curl` 跑一次端到端流程再收工**——這個 session 兩次都是靠這一步抓到真正的生產環境 bug。
3. **（後續 session 追加）MySQL `DATETIME` 不記錄時區，EF Core 讀回的 `DateTime.Kind` 會變成 `Unspecified`，導致 JSON 序列化漏掉 `Z` 尾碼、前端所有 `new Date(...)` 都會誤判成瀏覽器本地時間**：使用者人工測試回報「TimeEntry 用 UTC+8 時間建立，History List 卻顯示成 UTC」。追下去發現這不是單純的前端時區換算問題——`GET /api/time-entries`、`GET /api/dashboard` 回傳的 `startTimeUtc`/`endTimeUtc` 字串本身就沒有 `Z`（例如 `"2026-09-17T06:00:00"`），用 `curl` 直接打 API 就能重現（SQLite/InMemory 測試完全測不出來，因為 SQLite provider 會把 `DateTimeKind` 正確序列化進 ISO 字串再讀回來，只有真正的 MySQL 才會遺失）。**修正方式**：`Data/AppDbContext.cs` 覆寫 `ConfigureConventions`，對所有 `DateTime` 屬性套用新增的 `Data/UtcDateTimeConverter.cs`（讀取時強制 `DateTime.SpecifyKind(v, DateTimeKind.Utc)`），因為這個專案的每一個 `DateTime` 欄位依 `AGENTS.md` 規則本來就都是 UTC，全域套用是安全的。用 `curl` 重新驗證 MySQL 開發資料庫，確認 API 回應已經帶 `Z`。同時順手把 `wwwroot/js/time-entry.js`（History List 的快捷日期範圍、清單顯示時間、新增/編輯表單）從瀏覽器本地時區改成讀取帳號時區（`GET /api/settings/timezone` + 新增的純函式模組 `wwwroot/js/timezone.mjs`，含 Node 單元測試 `Tests/Unit/timezone.test.mjs`），對齊 Dashboard 既有的帳號時區顯示模式——這是這次 bug report 的第二層問題（第 12 項 Timezone settings 文件本來就記著這個已知缺口）。**這裡順手把 Timer 卡片（`timer.js`）也列進了「尚未改用帳號時區」的待辦清單，但那個假設沒有先查證程式碼——後續 session 已證實是誤判，見下面第 4 點。**
4. **（後續 session 追加）「Timer 卡片仍用瀏覽器本地時區」是一個沒查證就寫進文件、又被後續幾輪更新照抄的錯誤結論**：使用者要求處理 `Docs/TODO.md` 裡這一項時，實際讀 `wwwroot/js/timer.js` 才發現它的即時顯示（`tick`/`formatClock`）從頭到尾只做「經過秒數」的算術（`Date.now() - startedAtUtc.getTime()`），從來沒有任何日曆日期/時區相關的計算可言——跟 `time-entry.js`（History List）當初的 bug 性質完全不同，過去的文件更新是看到 `timer.js` 裡也有 `new Date()`/`.toISOString()` 就直接套用同一個結論，沒有先確認那段程式碼實際在算什麼。**教訓：文件裡「還沒做」的待辦項目，接手前也要重新對照程式碼確認還成立，不能只信之前 session 寫的結論，尤其是被連續照抄好幾輪的項目。** 順手修好一個這次真正找到、性質完全不同的小 bug：`formatClock` 原本借道 `new Date(seconds*1000).toISOString().slice(11,19)` 換算 `HH:mm:ss`，計時器一旦連續跑滿 24 小時（`AGENTS.md` 明講長時數的 TimeEntry 是允許的）就會在 86400 秒整數處繞回 `00:00:00`。修正方式：抽成 `wwwroot/js/timer-state.mjs` 的純函式 `formatClock`，改用整數除法/取餘運算（不再經過 `Date`），新增 `Tests/Unit/timer-state.test.mjs`（含 86400 秒／90061 秒的迴歸測試）。已用 curl 對真正的開發伺服器跑過 start/status/stop 全流程確認 API 契約（`startedAtUtc` 帶 `Z`）沒有受影響。

---

## 環境操作備忘

- **本機開發用的 MySQL 連線字串與 JWT 簽章金鑰放在 `dotnet user-secrets`**（`dotnet user-secrets list` 可以看鍵名，不要印出值）。`Program.cs` 啟動時會檢查這兩個設定，缺了會直接丟例外中止啟動。
- **啟動開發伺服器**：`ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5180 dotnet run --no-build`，建議用 `nohup ... > log 2>&1 & disown` 背景執行並把輸出導到檔案，否則工具的背景任務包裝有時會在指令列結束時一併把子行程帶走。**每次要重新編譯前，記得先停掉還在跑的 dev server**（`Stop-Process -Id <pid> -Force`），否則 `Lyubishchev Time Management.exe` 會被鎖住導致 `dotnet build` 失敗（`MSB3021`/`MSB3027`）。
- **這個 repo 沒有 `.sln` 檔**，每個測試專案都是各自獨立用 `dotnet build`/`dotnet test` 在自己的資料夾裡跑，不需要（也不能）註冊進解決方案檔。
- **不要同時對同一個主專案跑多個平行的 `dotnet test`/`dotnet build`**：三個測試專案都透過 `ProjectReference` 共用同一個主專案輸出，平行跑會撞到檔案鎖，導致警告、重試、甚至看起來卡住。想一次驗證全部，請**依序**跑：`AuthFlow.Tests` → `TimerFlow.Tests` → `TimeEntryFlow.Tests`（或反過來，但不要同時）。
- Windows 上的 `tasklist`/`Get-Process` 輸出在這個殼層編碼下可能會亂碼（繁體中文訊息被解成亂碼），這是顯示問題，不影響指令實際效果。

---

## 開始接手前建議的檢查清單

1. `git log --oneline -5` 確認目前在 RWD 切版完成之後（若使用者又做了其他修改，先弄清楚差異）。
2. 讀 `AGENTS.md` 全文一次（規則沒有變過，但務必自己確認，不要只信這份摘要）。
3. 讀 `Docs/TODO.md` 確認目前狀態（這份文件比本摘要新，衝突時以 `TODO.md` 為準）。
4. 依使用者這次想做的項目，讀對應的 `Docs/*.md` 設計文件（`JWT.md`／`RateLimitingAndAuthLogging.md`／`RunningTimerStartStop.md`／`TimeEntryCrud.md`／`superpowers/specs/2026-09-17-category-tag-management-design.md`／[`TimezoneAndAggregation.md`](TimezoneAndAggregation.md)／[`Dashboard.md`](Dashboard.md)／[`CalendarView.md`](CalendarView.md)／[`Report.md`](Report.md)／[`CsvExport.md`](CsvExport.md)／[`Rwd.md`](Rwd.md)）取得可重用的既有模式與已知的取捨/限制。
5. 動工前先 `dotnet build "Lyubishchev Time Management.csproj"` 一次確認目前基準是綠的，再依序跑三個測試專案（`AuthFlow.Tests`、`TimerFlow.Tests`、`TimeEntryFlow.Tests`，最後這個數字最容易隨新功能成長，實際跑一次以現況為準——完成 RWD 後是 105 個）確認全過，作為「改動前」的基準線；有動到任一頁面的 `Views/*.cshtml`、`wwwroot/js/dashboard*.mjs`/`report*.js`、`Views/Shared/_Layout.cshtml`/`_MobileNavigation.cshtml` 的話再額外跑 `node --test Tests/Unit/*.test.mjs`（目前 27 個）。
6. 做完之後：更新 `Docs/TODO.md`、視情況新增一份 `Docs/<功能名稱>.md`（比照既有文件的格式：設計思路 → 整體流程 → 檔案清單與內容 → 尚未涵蓋的部分），並在自動化測試全過之後，額外跑一次真實伺服器手動驗證再回報完成。
