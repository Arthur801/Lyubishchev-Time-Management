# TODO List

依照 `AGENTS.md` / `Docs/system_design_document.md` 的 Implementation Order 整理，最後更新於 2026-09-17。JWT Cookie 驗證流程已於 `feat/jwt-cookie-auth-flow` 分支完成並合併進 `main`，細節請見 [`Docs/JWT.md`](JWT.md)。Login/Register rate limiting 與 auth failure 記錄已完成，細節請見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)。RunningTimer Start/Stop 核心計時功能已完成，細節請見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)。

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
- [ ] `Services/TimeEntryService.cs`（空殼）
- [ ] `Controllers/Api/TimeEntryApiController.cs`（空殼，`/api/time-entries` 系列 endpoint 均未實作）
- [x] `Controllers/TimeEntryController.cs` + `Views/TimeEntry/Index.cshtml`（List View 頁面殼已完成，但目前顯示的是前端寫死的 mock data，非真實資料庫資料）

## 5. Category
- [ ] `Services/CategoryService.cs`（空殼）
- [ ] `Controllers/CategoryController.cs`（空殼，無 View）
- [ ] `Controllers/Api/CategoryApiController.cs`（空殼，`/api/categories` 系列均未實作）

## 6. Tag + TimeEntryTag
- [ ] `Services/TagService.cs`（空殼）
- [ ] `Controllers/TagController.cs`（空殼，無 View）
- [ ] `Controllers/Api/TagApiController.cs`（空殼，`/api/tags` 系列均未實作）

## 7. History List
- [x] List View 頁面（`Views/TimeEntry/Index.cshtml`、`history.css`、`time-entry.js`）已完成，目前為 mock data 前端原型
- [ ] 串接真實 API/資料庫、分頁（建議 50 筆/頁）、編輯/刪除功能落地

## 8. Calendar View
- [ ] 尚未開始（Controller/View/JS 皆無）
- [ ] `wwwroot/js/calendar.js` 待確認/建立
- [ ] 需遵守「只查詢可視範圍」的 overlap 查詢規則

## 9. TimeAggregationService
- [ ] `Services/TimeAggregationService.cs`（空殼）— 需集中處理時區轉換、interval intersection、duration 計算、Category/Tag 聚合，供 Dashboard 與 Report 共用

## 10. Dashboard
- [x] `Controllers/DashboardController.cs` + `Views/Dashboard/Index.cshtml` 頁面殼已完成
- [ ] 目前顯示的統計資料為前端 mock data（`dashboard-state.mjs`），尚未串接 `DashboardService`/`TimeAggregationService`/真實資料庫
- [ ] `Services/DashboardService.cs`（空殼）
- [ ] `Controllers/Api/DashboardApiController.cs`（空殼，`/api/dashboard` 未實作）

## 11. Report
- [ ] `Services/ReportService.cs`（空殼）
- [ ] `Controllers/ReportController.cs`（空殼，無 View）
- [ ] `Controllers/Api/ReportApiController.cs`（空殼，`/api/reports/category`、`/api/reports/tag` 未實作）
- [ ] Category 圓餅圖、Tag 長條圖尚未開始

## 12. Timezone settings
- [ ] `Services/UserSettingsService.cs`（空殼）
- [ ] `Controllers/SettingsController.cs`（空殼，無 View）
- [ ] `Controllers/Api/SettingsApiController.cs`（空殼，`PATCH /api/settings/timezone` 未實作）

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
- [~] C# Unit Tests：Timer state 已隨 `TimerFlow.Tests` 一併覆蓋；duration、overlap、interval intersection、cross-midnight、timezone conversion、Category/Tag aggregation 待 `TimeAggregationService`（第 9 項）實作後補上
- [x] Integration Tests：Register/Login 已完成（`Tests/AuthFlow.Tests`，12 個測試：`ReturnUrlPolicyTests` 7 個 + `AuthServiceTests` 5 個，使用 EF Core InMemory）
- [x] Integration Tests：Start/Stop Timer、並發 Stop 已完成（`Tests/TimerFlow.Tests`，7 個測試，使用 SQLite in-memory；細節見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)）
- [ ] Integration Tests：TimeEntry CRUD、Category 刪除 SET NULL、Tag 刪除只移除關聯、跨使用者存取拒絕 — 尚未開始

---

## 已完成項目摘要
- 資料模型與 EF Core 設定（Users/RunningTimers/Categories/Tags/TimeEntries/TimeEntryTags）完全符合設計文件
- 專案資料夾結構、Controller/Service/JS 檔名均已依文件建立（多數為空殼待實作）
- JWT Cookie 驗證流程（Register/Login/Logout + `[Authorize]` 頁面保護 + CSRF），詳見 [`Docs/JWT.md`](JWT.md)
- Login/Register rate limiting（同 IP 5 分鐘 10 次）與 auth 事件記錄（`IAuthEventLogger`），詳見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)
- RunningTimer Start/Stop 核心計時功能（含單一交易的 Stop 流程、並發 Stop 只建立一筆 TimeEntry、Dashboard 計時卡片已串接真實 API），詳見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)
- 頁面殼：Login、Register、Dashboard、TimeEntry List（前端原型，Dashboard/TimeEntry 已受 `[Authorize]` 保護；Dashboard 的計時器已是真實資料，其餘統計卡片/圖表仍是 mock data，尚未串接真實後端資料）
- Secrets 管理：連線字串、JWT 簽章金鑰皆使用 `dotnet user-secrets`，未提交至 Git

## 下一步建議優先順序
1. Manual TimeEntry CRUD + `TimeAggregationService`（讓 Dashboard/History 串接真實資料，取代 mock data）
2. Category、Tag + TimeEntryTag（讓計時器 Stop 之後可以補上分類與標籤）
3. 全域例外處理與其餘 `Infrastructure/Logging` 事件（DB 錯誤、timer transaction failure、CSV export failure）
