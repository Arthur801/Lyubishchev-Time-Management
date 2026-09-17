# TODO List

依照 `AGENTS.md` / `Docs/system_design_document.md` 的 Implementation Order 整理，最後更新於 2026-09-17。JWT Cookie 驗證流程已於 `feat/jwt-cookie-auth-flow` 分支完成並合併進 `main`，細節請見 [`Docs/JWT.md`](JWT.md)。Login/Register rate limiting 與 auth failure 記錄已完成，細節請見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)。RunningTimer Start/Stop 核心計時功能已完成，細節請見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)。Manual TimeEntry CRUD（含 Category 指派、Tag inline 建立、History List 前端）已完成，細節請見 [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md)。Category、Tag 獨立管理頁面與 CRUD API 已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-category-tag-management-design.md`](superpowers/specs/2026-09-17-category-tag-management-design.md)，實作細節見 `Docs/HANDOFF.md`。Timezone settings 與 TimeAggregationService 已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-timezone-and-aggregation-design.md`](superpowers/specs/2026-09-17-timezone-and-aggregation-design.md)，實作細節見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)。Dashboard 真實資料串接已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-dashboard-design.md`](superpowers/specs/2026-09-17-dashboard-design.md)，實作細節見 [`Docs/Dashboard.md`](Dashboard.md)。

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
- 詳細實作說明見 [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md)

## 8. Calendar View
- [ ] 尚未開始（Controller/View/JS 皆無）
- [ ] `wwwroot/js/calendar.js` 待確認/建立
- [ ] 需遵守「只查詢可視範圍」的 overlap 查詢規則

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
- [ ] `Services/ReportService.cs`（空殼）
- [ ] `Controllers/ReportController.cs`（空殼，無 View）
- [ ] `Controllers/Api/ReportApiController.cs`（空殼，`/api/reports/category`、`/api/reports/tag` 未實作）
- [ ] Category 圓餅圖、Tag 長條圖尚未開始

## 12. Timezone settings
- [x] `Services/UserSettingsService.cs`（`GetAsync`/`UpdateAsync`，以 `TimeZoneCatalog` 白名單驗證，只改 `User.TimeZoneId`/`UpdatedAtUtc`，不動任何 `TimeEntry` UTC 欄位）
- [x] `Controllers/SettingsController.cs` + `Views/Settings/Index.cshtml`（受 `[Authorize]` 保護的設定頁，選項由後端 API 載入，瀏覽器偵測時區只作提示不自動送出）
- [x] `Controllers/Api/SettingsApiController.cs`（`GET`/`PATCH /api/settings/timezone` 已實作，`[Authorize]` + PATCH `[ValidateAntiForgeryToken]`）
- [x] `Infrastructure/Time/TimeZoneCatalog.cs`（13 個常用 IANA 時區白名單，供 `UserSettingsService`/`TimeAggregationService` 共用）
- 詳細實作說明見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)

## 13. CSV export
- [ ] `Services/CsvExportService.cs`（空殼）
- [ ] `Infrastructure/Csv/CsvWriter.cs`（空殼）
- [ ] `/api/time-entries/export` 未實作

## 14. RWD
- [x] Dashboard / History / Login / Register 頁面已有響應式樣式（各自頁面 CSS 內的 media query）
- [ ] `wwwroot/css/responsive.css` 目前為空檔案且未使用，樣式分散在各頁面 CSS 中，與文件建議的集中管理方式不同（非必要修正，功能上不算錯）
- [ ] Calendar / Report 等尚未建立頁面的 RWD 待補

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
- [x] Integration Tests：Dashboard（Today/Week/Month/Custom 比較期、不同長度月份、無效 preset/date range、preset 與 custom 同給、空資料、最近五筆排序、跨使用者隔離、Category/Tag totals 透傳）已完成，`Tests/TimeEntryFlow.Tests` 目前共 83 個測試，細節見 [`Docs/Dashboard.md`](Dashboard.md)。前端純函式與靜態標記另有 `Tests/Unit/dashboard-frontend.test.mjs`（Node `node:test`，4 個測試）

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

## 下一步建議優先順序
1. Report（第 11 項）串接 `TimeAggregationService`，延續 `DashboardService` 的「只組裝、不重寫聚合邏輯」模式（Category 圓餅圖、Tag 長條圖）
2. 重新檢視 Timer 卡片（`timer.js`）與 History List（`time-entry.js`）目前用瀏覽器本地時區計算日期範圍的簡化做法，改用使用者在 `/Settings` 設定的時區（Dashboard 的最近活動時間已經改好，可參考同一個模式）
3. 全域例外處理與其餘 `Infrastructure/Logging` 事件（DB 錯誤、timer transaction failure、CSV export failure）
