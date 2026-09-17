# TODO List

依照 `AGENTS.md` / `Docs/system_design_document.md` 的 Implementation Order 整理，最後更新於 2026-09-17。JWT Cookie 驗證流程已於 `feat/jwt-cookie-auth-flow` 分支完成並合併進 `main`，細節請見 [`Docs/JWT.md`](JWT.md)。Login/Register rate limiting 與 auth failure 記錄已完成，細節請見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)。RunningTimer Start/Stop 核心計時功能已完成，細節請見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)。Manual TimeEntry CRUD（含 Category 指派、Tag inline 建立、History List 前端）已完成，細節請見 [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md)。Category、Tag 獨立管理頁面與 CRUD API 已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-category-tag-management-design.md`](superpowers/specs/2026-09-17-category-tag-management-design.md)，實作細節見 `Docs/HANDOFF.md`。Timezone settings 與 TimeAggregationService 已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-timezone-and-aggregation-design.md`](superpowers/specs/2026-09-17-timezone-and-aggregation-design.md)，實作細節見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)。Dashboard 真實資料串接已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-dashboard-design.md`](superpowers/specs/2026-09-17-dashboard-design.md)，實作細節見 [`Docs/Dashboard.md`](Dashboard.md)。Report（Category 圓餅圖、Tag 長條圖）已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-report-design.md`](superpowers/specs/2026-09-17-report-design.md)，實作細節見 [`Docs/Report.md`](Report.md)。CSV export 已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-csv-export-design.md`](superpowers/specs/2026-09-17-csv-export-design.md)，實作細節見 [`Docs/CsvExport.md`](CsvExport.md)。

狀態標記：`[x]` 完成、`[~]` 部分完成、`[ ]` 未開始

---

## 1. Project / MySQL / EF Core
- [x] 專案建立、EF Core + MySql.EntityFrameworkCore 設定
- [x] `AppDbContext` 與 `Data/Configurations/*` 完成，Entity（User/RunningTimer/TimeEntry/Category/Tag/TimeEntryTag）符合設計文件 schema、索引、CHECK 約束

## 2. User / Register / Login / Logout / JWT
- [x] `Security/JwtTokenService.cs`、`CurrentUserService.cs`、`AuthConstants.cs`、`JwtOptions.cs`、`ReturnUrlPolicy.cs` 已實作
- [x] `Services/AuthService.cs`（Register/Login，使用 `PasswordHasher<User>` 雜湊密碼並簽發 JWT）
- [x] `Program.cs` 已註冊 JWT Bearer 驗證（從 `ltm_auth` Cookie 讀取 token）、`UseAuthentication()`、Antiforgery
- [x] `AccountController` 已補上 `POST Login/Register/Logout`，簽發/清除 Secure+HttpOnly Cookie
- [x] `wwwroot/js/auth.js` 已改為透過 `fetch` 實際呼叫 API，並附上 CSRF header
- [x] `DashboardController`、`TimeEntryController` 已套用 `[Authorize]`，未登入使用者存取會被導向 `/Account/Login`
- [x] CSRF/Antiforgery：header-based token，已接到 `_Layout.cshtml`/`_AuthLayout.cshtml` 與 `auth.js`
- [x] Login/Register rate limiting（見第 15 項）
- 詳細實作說明見 [`Docs/JWT.md`](JWT.md)

## 3. RunningTimer / Start / Stop
- [x] `Services/TimerService.cs`（`GetStatusAsync`/`StartAsync`/`StopAsync`，含 `IClock` 抽象化時間）
- [x] `Controllers/Api/TimerApiController.cs`（`GET /api/timer`、`POST /api/timer/start`、`POST /api/timer/stop`，`[Authorize]` + CSRF）
- [x] Stop Timer 的 DB transaction 流程（讀取 → 建立 TimeEntry → 刪除 RunningTimer → commit，並發衝突以 `DbUpdateConcurrencyException` 偵測後 rollback，確保只有一筆 TimeEntry）
- [x] `wwwroot/js/timer.js` 已串接真實 API（Dashboard 計時卡片），取代原本 `dashboard.js` 內的前端模擬計時邏輯
- 詳細實作說明見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)

## 4. Manual TimeEntry CRUD
- [x] `Services/TimeEntryService.cs`（`ListAsync`/`CreateAsync`/`UpdateAsync`/`DeleteAsync`/`GetCategoryOptionsAsync`，含 Category 擁有權驗證、Tag inline find-or-create 併發處理）
- [x] `Controllers/Api/TimeEntryApiController.cs`（`GET`/`POST`/`PATCH {id}`/`DELETE {id}` `/api/time-entries`，`[Authorize]` + CSRF；`export` 不在此範圍，見第 13 項）
- [x] `Controllers/TimeEntryController.cs` + `Views/TimeEntry/Index.cshtml`（History List 已串接真實 API，含新增/編輯表單、分頁、分類/搜尋篩選，取代原本的前端 mock data）
- 詳細實作說明見 [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md)

## 5. Category
- [x] `Services/CategoryService.cs`（`ListAsync`/`CreateAsync`/`UpdateAsync`/`DeleteAsync`，以持久化的 `NormalizedName`（Trim + 不分大小寫）做同使用者唯一性檢查，含 `DbUpdateException` 併發衝突處理）
- [x] `Controllers/CategoryController.cs` + `Views/Category/Index.cshtml`（受 `[Authorize]` 保護的管理頁，資料完全由前端呼叫 API 取得，Controller 不碰 `AppDbContext`）
- [x] `Controllers/Api/CategoryApiController.cs`（`GET`/`POST`/`PATCH {id}`/`DELETE {id}` `/api/categories`，`[Authorize]` + CSRF）
- [x] `TimeEntryService.GetOwnedCategoryAsync`/`GetCategoryOptionsAsync` 沿用不變，供 Manual TimeEntry CRUD 使用

## 6. Tag + TimeEntryTag
- [x] `Services/TagService.cs`（`ListAsync`/`CreateAsync`/`UpdateAsync`/`DeleteAsync`，同樣以 `NormalizedName` 做唯一性檢查，刪除交由既有 `TimeEntryTags` 的 `ON DELETE CASCADE` FK 處理，不額外刪除 TimeEntry）
- [x] `Controllers/TagController.cs` + `Views/Tag/Index.cshtml`（管理頁不含色彩欄位，其餘與 Category 頁一致）
- [x] `Controllers/Api/TagApiController.cs`（`GET`/`POST`/`PATCH {id}`/`DELETE {id}` `/api/tags`，`[Authorize]` + CSRF）
- [x] `TimeEntryService.NormalizeTagNames`/`FindOrCreateTagsAsync` 已改用 `Infrastructure/Text/ResourceName.TryNormalize` 正規化並以 `NormalizedName` 查找，inline 建立與管理頁建立共用同一套唯一性規則（含併發處理）

## 7. History List
- [x] List View 頁面（`Views/TimeEntry/Index.cshtml`、`history.css`、`time-entry.js`）已串接真實 API，含分頁（50 筆/頁）、日期範圍/分類/搜尋篩選、新增/編輯表單、刪除，取代原本的 mock data 原型
- [x] `time-entry.js` 的日期範圍快捷篩選、清單分組/顯示時間、新增/編輯表單的時間欄位已全部改用帳號時區（`GET /api/settings/timezone` + `wwwroot/js/timezone.mjs`），不再用瀏覽器本地時區，修正建立/編輯時間與顯示時間不一致的 bug，細節見 `Docs/HANDOFF.md`
- 詳細實作說明見 [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md)

## 8. Calendar View
- [x] `/TimeEntry` 頁面新增 List／Calendar tab 切換（`Views/TimeEntry/Index.cshtml`），Calendar 唯讀、桌面週時間軸／手機單日時間軸
- [x] `wwwroot/js/calendar.js` + `wwwroot/js/calendar-state.mjs`（純函式：跨午夜切段、重疊分欄）+ `wwwroot/css/calendar.css`
- [x] 只查詢可視範圍：重用既有 `GET /api/time-entries` overlap 查詢（`TimeEntryService.ListAsync` 本來就有 overlap 條件、`UserId` 過濾、`pageSize` clamp 在 200），沒有新增後端程式碼
- [x] 顯示時間換算帳號時區：`wwwroot/js/timezone.mjs` 新增 `zonedDayUtcBounds`/`addZonedDays`/`weekdayOfDate`/`getZonedDateParts`
- 詳細實作說明見 [`Docs/CalendarView.md`](CalendarView.md)

## 9. TimeAggregationService
- [x] `Services/TimeAggregationService.cs`（`GetRangeForPreset`/`AggregateAsync`，集中處理時區轉換、UTC 半開區間 interval intersection、跨午夜/DST 逐日切分、Category（含未分類）/Tag 聚合）已被 Dashboard（第 10 項）串接使用，尚待 Report（第 11 項）串接
- 詳細實作說明見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)

## 10. Dashboard
- [x] `Controllers/DashboardController.cs` + `Views/Dashboard/Index.cshtml`（頁面殼、空狀態與載入中預留字串，Timer 卡片維持獨立不變）
- [x] `Services/DashboardService.cs`（`GetAsync`：preset/custom range 互斥驗證、呼叫 `TimeAggregationService` 取得本期與相鄰比較期、最近五筆活動組裝，不重寫任何聚合邏輯）
- [x] `Controllers/Api/DashboardApiController.cs`（`GET /api/dashboard`，`[Authorize]`，`preset=today|week|month` 或 `startDate`/`endDate` custom range，400 `INVALID_RANGE_PRESET`/`INVALID_DATE_RANGE`）
- [x] `wwwroot/js/dashboard.js`/`dashboard-state.mjs` 已改為真實 fetch，`dashboard-state.mjs` 的 mock snapshot 與 `addCompletedEntry` 已移除，只保留 `formatDuration`/`isValidDateRange` 兩個純函式供 Node 測試使用
- 詳細實作說明見 [`Docs/Dashboard.md`](Dashboard.md)

## 11. Report
- [x] `Services/ReportService.cs`（`GetCategoryAsync`/`GetTagAsync`：只轉接 preset/custom range 給 `TimeAggregationService`，不重寫任何聚合邏輯；preset/custom 互斥驗證與 `DashboardService` 一致，唯一差異是缺省時 preset 預設 `month` 而非回錯）
- [x] `Controllers/ReportController.cs` + `Views/Report/Index.cshtml`（受 `[Authorize]` 保護的頁面殼，Category 圓餅圖 + Tag 長條圖，桌面雙欄／900px 以下單欄堆疊）
- [x] `Controllers/Api/ReportApiController.cs`（`GET /api/reports/category`、`GET /api/reports/tag`，`[Authorize]`，400 `INVALID_RANGE_PRESET`/`INVALID_DATE_RANGE`）
- [x] `wwwroot/js/report.js`（平行呼叫兩端點、request sequence 忽略舊回應、Category legend 顯示百分比、Tag bar 不顯示百分比）+ `wwwroot/css/report.css`
- 詳細實作說明見 [`Docs/Report.md`](Report.md)

## 12. Timezone settings
- [x] `Services/UserSettingsService.cs`（`GetAsync`/`UpdateAsync`，以 `TimeZoneCatalog` 白名單驗證，只改 `User.TimeZoneId`/`UpdatedAtUtc`，不動任何 `TimeEntry` UTC 欄位）
- [x] `Controllers/SettingsController.cs` + `Views/Settings/Index.cshtml`（受 `[Authorize]` 保護的設定頁，選項由後端 API 載入，瀏覽器偵測時區只作提示不自動送出）
- [x] `Controllers/Api/SettingsApiController.cs`（`GET`/`PATCH /api/settings/timezone` 已實作，`[Authorize]` + PATCH `[ValidateAntiForgeryToken]`）
- [x] `Infrastructure/Time/TimeZoneCatalog.cs`（13 個常用 IANA 時區白名單，供 `UserSettingsService`/`TimeAggregationService` 共用）
- 詳細實作說明見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)

## 13. CSV export
- [x] `Infrastructure/Csv/CsvWriter.cs`（無狀態 RFC 4180 編碼器，UTF-8 BOM、CRLF、escape、公式中和）
- [x] `Services/CsvExportService.cs`（`ExportAsync`：不分頁的 owned 查詢、帳號時區轉換、時長用 UTC 差值、檔名依範圍決定）
- [x] `GET /api/time-entries/export`（`TimeEntryApiController.Export`，`[Authorize]`，複用清單的 `INVALID_TIME_RANGE` 驗證，不掛 CSRF）
- [x] History 頁「匯出 CSV」按鈕（`time-entry.js` 用 `window.location.assign` 導覽下載，篩選值與清單一致，只是不分頁）
- 詳細實作說明見 [`Docs/CsvExport.md`](CsvExport.md)

## 14. RWD
- [x] Dashboard / History / Login / Register 頁面已有響應式樣式（各自頁面 CSS 內的 media query）
- [ ] `wwwroot/css/responsive.css` 目前為空檔案且未使用，樣式分散在各頁面 CSS 中，與文件建議的集中管理方式不同（非必要修正，功能上不算錯）
- [x] Report 頁面已有響應式樣式（`report.css`，桌面雙欄／900px 以下單欄堆疊，沿用 `dashboard.css` 的 donut/legend/tag-chart 元件）

## 15. Error handling / Logging / Rate limit
- [ ] 全域例外處理（Global Exception Handler）尚未確認是否已設定
- [~] `Infrastructure/Logging/`：`IAuthEventLogger`/`AuthEventLogger` 已完成 auth failure/success 記錄，其餘 log 事件（startup/shutdown、DB 錯誤、timer transaction failure、CSV export failure）待對應功能實作時補上。詳見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)
- [x] Login/Register rate limiting（`Microsoft.AspNetCore.RateLimiting`，同 IP 5 分鐘內限 10 次），詳見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)
- [x] CSRF/Antiforgery 已設定（header-based，`Account` 的 Login/Register/Logout 已套用 `[ValidateAntiForgeryToken]`），其餘未來的狀態變更 API 仍需比照套用

## 16. Nginx / EC2 / Backup
- [ ] 尚未開始（部署階段，預期在專案後期處理）

---

## 測試（AGENTS.md Testing Priorities）
- [x] C# Unit Tests：Timer state（`TimerFlow.Tests`）、duration/overlap（`TimeEntryFlow.Tests`）已覆蓋；interval intersection 的跨午夜情境已隨 `ListAsync` 的邊界測試覆蓋；timezone conversion、Category/Tag aggregation（含未分類、DST 春季/秋季）已隨 `TimeAggregationService`（第 9 項）補上，細節見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)
- [x] Integration Tests：Register/Login 已完成（`Tests/AuthFlow.Tests`，12 個測試：`ReturnUrlPolicyTests` 7 個 + `AuthServiceTests` 5 個，使用 EF Core InMemory）
- [x] Integration Tests：Start/Stop Timer、並發 Stop 已完成（`Tests/TimerFlow.Tests`，7 個測試，使用 SQLite in-memory；細節見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)）
- [x] Integration Tests：TimeEntry CRUD、Category 刪除 SET NULL、Tag 刪除只移除關聯、跨使用者存取拒絕已完成（`Tests/TimeEntryFlow.Tests`，細節見 [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md)）
- [x] Integration Tests：Category/Tag 管理（唯一鍵衝突、跨使用者拒絕、併發重複建立、刪除語意）已完成，使用共用的 `TestDatabase` SQLite in-memory fixture
- [x] Integration/Unit Tests：Timezone settings、TimeAggregationService（白名單、跨使用者隔離、跨午夜切分、DST 春季/秋季整天時長、未分類桶、Category/Tag 聚合）已完成，細節見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)
- [x] Integration Tests：Dashboard（Today/Week/Month/Custom 比較期、不同長度月份、無效 preset/date range、preset 與 custom 同給、空資料、最近五筆排序、跨使用者隔離、Category/Tag totals 透傳）已完成，細節見 [`Docs/Dashboard.md`](Dashboard.md)。前端純函式與靜態標記另有 `Tests/Unit/dashboard-frontend.test.mjs`（Node `node:test`，4 個測試）
- [x] Integration Tests：Report（preset 缺省時預設 month、today/week/month preset、custom range、preset 與 custom 同給/半給皆無效、無效日期範圍、Uncategorized 顏色、Tag additive 加總與排序、空資料、跨使用者隔離、Category 排序）已完成，細節見 [`Docs/Report.md`](Report.md)
- [x] Unit Tests：Calendar View 的跨午夜切段與重疊分欄純函式（`Tests/Unit/calendar-state.test.mjs`，5 個測試）、`timezone.mjs` 新增的日期運算函式（`Tests/Unit/timezone.test.mjs`，新增 4 個測試），細節見 [`Docs/CalendarView.md`](CalendarView.md)。`Tests/Unit/` 目前共 23 個 Node 測試（`node --test Tests/Unit/*.test.mjs`）
- [x] Unit/Integration Tests：CSV export（`CsvWriterTests` 3 個：BOM/CRLF/escape/公式中和/空資料；`CsvExportServiceTests` 5 個：完整排序、跨午夜完整一筆、201 筆無分頁上限、跨使用者隔離＋空結果、New York 春季 DST 時長），`Tests/TimeEntryFlow.Tests` 目前共 105 個測試，細節見 [`Docs/CsvExport.md`](CsvExport.md)

---

## 已完成項目摘要
- 資料模型與 EF Core 設定（Users/RunningTimers/Categories/Tags/TimeEntries/TimeEntryTags）完全符合設計文件
- 專案資料夾結構、Controller/Service/JS 檔名均已依文件建立（多數為空殼待實作）
- JWT Cookie 驗證流程（Register/Login/Logout + `[Authorize]` 頁面保護 + CSRF），詳見 [`Docs/JWT.md`](JWT.md)
- Login/Register rate limiting（同 IP 5 分鐘 10 次）與 auth 事件記錄（`IAuthEventLogger`），詳見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)
- RunningTimer Start/Stop 核心計時功能（含單一交易的 Stop 流程、並發 Stop 只建立一筆 TimeEntry、Dashboard 計時卡片已串接真實 API），詳見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)
- Manual TimeEntry CRUD（Create/Update/Delete/List，含分頁、日期範圍 overlap 篩選、Category 擁有權驗證、Tag inline find-or-create 併發處理），History List 頁面已完全串接真實 API 並新增建立/編輯表單，詳見 [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md)
- 頁面殼：Login、Register、Dashboard、TimeEntry List（Dashboard/TimeEntry 已受 `[Authorize]` 保護；Dashboard 的計時器、TimeEntry History 與統計卡片/圖表皆已是真實資料，mock data 已全部移除）
- Category、Tag 獨立管理頁面與 CRUD API（建立/改名/刪除，`CategoryService`/`TagService`，以持久化 `NormalizedName` 做 Trim + 不分大小寫的同使用者唯一性約束；Category 刪除沿用 `ON DELETE SET NULL`、Tag 刪除沿用 `ON DELETE CASCADE`；inline Tag 建立與管理頁共用同一套正規化規則），詳見 `Docs/HANDOFF.md`
- Timezone settings（`TimeZoneCatalog` 白名單、`UserSettingsService`、`/Settings` 頁面與 `GET`/`PATCH /api/settings/timezone`）與 `TimeAggregationService`（本地日期範圍轉 UTC 半開區間、跨午夜/DST 逐日切分、Category/Tag 聚合，供 Dashboard/Report 共用），詳見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)
- Dashboard 真實資料串接（`DashboardService` 組裝本期/相鄰比較期/最近五筆活動，`GET /api/dashboard`，`dashboard.js` 移除全部 mock data，最近活動時間改用帳號時區顯示），詳見 [`Docs/Dashboard.md`](Dashboard.md)
- Secrets 管理：連線字串、JWT 簽章金鑰皆使用 `dotnet user-secrets`，未提交至 Git
- **修正一個影響全站的 DateTime 序列化 bug**：`AppDbContext` 新增 `ConfigureConventions` + `Data/UtcDateTimeConverter.cs`，讓所有從 MySQL 讀回的 `DateTime` 都強制標記 `DateTimeKind.Utc`（MySQL `DATETIME` 欄位不記錄時區，EF Core 讀回時預設是 `Unspecified`，導致 JSON 序列化漏掉 `Z` 尾碼，前端任何 `new Date(...)` 都會誤判成瀏覽器本地時間）；同時把 History List（`time-entry.js`）改成用帳號時區（見上方第 7 項），細節見 `Docs/HANDOFF.md`
- Calendar View（第 8 項）：`/TimeEntry` 新增 List／Calendar tab 切換，桌面週時間軸／手機單日時間軸，唯讀、重用既有 `GET /api/time-entries` overlap 查詢，沒有新增後端程式碼，詳見 [`Docs/CalendarView.md`](CalendarView.md)
- Report（第 11 項）：`ReportService` 只轉接 range 給 `TimeAggregationService`（不重寫聚合邏輯），`GET /api/reports/category`/`GET /api/reports/tag` 預設本月，`report.js` 平行讀取兩端點並同步渲染 Category 圓餅圖（含百分比 legend）與 Tag 長條圖（無百分比，因為可重複累計），側欄「報表」`href="#"` 已全站改指向 `/Report`，詳見 [`Docs/Report.md`](Report.md)
- CSV export（第 13 項）：`GET /api/time-entries/export` 不分頁輸出 History 目前篩選值命中的全部 TimeEntry，UTF-8 BOM RFC 4180、帳號時區顯示、時長用 UTC 差值不受 DST 影響，不重用 `TimeAggregationService`（那是彙總用的區間裁切，匯出需要完整明細）；修正了計畫範例程式碼裡一個真的 BOM bug（`Encoding.GetBytes()` 不會自動加 BOM，要手動接上 `GetPreamble()`），詳見 [`Docs/CsvExport.md`](CsvExport.md)

## 下一步建議優先順序
1. Timer 卡片（`timer.js`）即時顯示目前仍用瀏覽器本地時區計算日期範圍，尚未改用使用者在 `/Settings` 設定的時區（History List、Calendar View、CSV export 都已改用帳號時區，可參考同一個模式：`wwwroot/js/timezone.mjs`）
2. Calendar View 與 Report 尚未在真的瀏覽器裡驗證過視覺效果（桌面週欄、320px 單日、focus 順序），這個 session 沒有可用的瀏覽器自動化工具，建議接手後優先補上
3. 全域例外處理與其餘 `Infrastructure/Logging` 事件（DB 錯誤、timer transaction failure、CSV export failure）——CSV export 本身已完成，但它失敗時的 log 事件還沒補（沿用專案既有慣例，跟其餘 logging 基礎設施一起做）
4. AGENTS.md Implementation Order 的第 1–13 項已全數完成，剩下第 14（RWD 集中化，非必要）、15（錯誤處理/日誌）、16（部署）
