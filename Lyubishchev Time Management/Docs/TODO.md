# TODO List

依照 `AGENTS.md` / `Docs/system_design_document.md` 的 Implementation Order 整理，最後更新於 2026-09-17。JWT Cookie 驗證流程已於 `feat/jwt-cookie-auth-flow` 分支完成並合併進 `main`，細節請見 [`Docs/JWT.md`](JWT.md)。Login/Register rate limiting 與 auth failure 記錄已完成，細節請見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)。RunningTimer Start/Stop 核心計時功能已完成，細節請見 [`Docs/RunningTimerStartStop.md`](RunningTimerStartStop.md)。Manual TimeEntry CRUD（含 Category 指派、Tag inline 建立、History List 前端）已完成，細節請見 [`Docs/TimeEntryCrud.md`](TimeEntryCrud.md)。Category、Tag 獨立管理頁面與 CRUD API 已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-category-tag-management-design.md`](superpowers/specs/2026-09-17-category-tag-management-design.md)，實作細節見 `Docs/HANDOFF.md`。Timezone settings 與 TimeAggregationService 已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-timezone-and-aggregation-design.md`](superpowers/specs/2026-09-17-timezone-and-aggregation-design.md)，實作細節見 [`Docs/TimezoneAndAggregation.md`](TimezoneAndAggregation.md)。Dashboard 真實資料串接已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-dashboard-design.md`](superpowers/specs/2026-09-17-dashboard-design.md)，實作細節見 [`Docs/Dashboard.md`](Dashboard.md)。Report（Category 圓餅圖、Tag 長條圖）已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-report-design.md`](superpowers/specs/2026-09-17-report-design.md)，實作細節見 [`Docs/Report.md`](Report.md)。CSV export 已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-csv-export-design.md`](superpowers/specs/2026-09-17-csv-export-design.md)，實作細節見 [`Docs/CsvExport.md`](CsvExport.md)。RWD 切版已完成，設計依據見 [`Docs/superpowers/specs/2026-09-17-rwd-design.md`](superpowers/specs/2026-09-17-rwd-design.md)，實作細節見 [`Docs/Rwd.md`](Rwd.md)。

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
- [x] **釐清並修正先前文件裡「Timer 卡片仍用瀏覽器本地時區」的不準確描述**：實際檢查後發現 `timer.js` 的即時顯示只做「經過秒數」的計時（`Date.now() - startedAtUtc`），從來不做任何跟日曆日期/時區相關的計算，過去幾輪文件更新把它跟 `time-entry.js` 的日期範圍 bug 混為一談是誤判。真正的既有問題是 `formatClock` 借道 `new Date(seconds*1000).toISOString().slice(11,19)` 換算 HH:mm:ss，一旦計時超過 24 小時會在 86400 秒整數處繞回 `00:00:00`（`AGENTS.md` 明講「Long TimeEntries are allowed」／時長可能超過 24 小時）。已抽成純函式 `wwwroot/js/timer-state.mjs` 的 `formatClock`（改用整數除法/取餘，不再經過 `Date`），修正這個溢位，並新增 `Tests/Unit/timer-state.test.mjs`（3 個測試，含 86400 秒與 90061 秒的迴歸案例）
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
- [x] 三種版面帶：桌面 `>=901px`（固定側欄）、平板/窄桌面 `721–900px`、手機 `<=720px`（皆為行動 header + 五項底部導覽），320px 為最小支援寬度
- [x] `Views/Shared/_MobileNavigation.cshtml`（共用局部檢視，`@model string` 決定 active 狀態）取代原本六個頁面各自重複的 `<nav class="mobile-nav">` 標記，新增涵蓋 Dashboard/歷史紀錄/報表/設定四個直接項目 + 「更多」（Category/Tag 經由此開啟，不再是行動裝置的死路）
- [x] `wwwroot/js/mobile-navigation.mjs`（`isMoreSection`、`createMoreSheetController` 純函式/工廠，原生 `<dialog>`、Escape/backdrop/關閉鈕都收斂進同一個 `close` 事件處理）+ `wwwroot/js/mobile-navigation.js`（DOM 選取後啟動，載入自 `_Layout.cshtml`）
- [x] `dashboard.css` 擴充為 shell 權威樣式：`.mobile-nav`/`.mobile-nav__link` 44px 觸控目標、`.dashboard-content` 底部 padding 保留 `calc(76px + env(safe-area-inset-bottom))` 空間給固定底部列、`.more-sheet` 底部彈出對話框（45% 遮罩、`min(76vh,620px)`、面板可捲動並含安全區 padding）、`.entry-modal` 在 `<=720px` 有 `calc(100dvh - 24px)` 上限並可內部捲動、`prefers-reduced-motion:reduce` 全域降低過場動畫；`.primary-button` 從 `category-tag.css` 移到這裡作為共用元件（原本 Settings 頁面完全沒載入 `category-tag.css`，儲存按鈕其實是無樣式的，順手修正）
- [x] Dashboard 側欄/行動 header 的品牌連結從 `href="#"` 改為 `asp-controller="Dashboard" asp-action="Index"`；六個頁面行動 header 移除原本無作用的 ⚙ icon button（`.icon-button` CSS 也一併移除，不留死程式碼）
- [x] `history.css`（篩選器手機滿版、`entry-name`/`entry-actions` 長文字換行、`icon-btn` 手機 44px）、`calendar.css`（週格保留內部捲動＋捲軸提示、日模式工具列手機滿版，不動既有 768px 的週/日切換門檻，因為那是 `calendar.js` 判斷資料抓取模式用的，不是純視覺斷點）、`report.css`（legend/tag row 允許換行但保持名稱與時長成對）、`category-tag.css`（長名稱 `overflow-wrap:anywhere`）、`auth.css`（`<=479px` 用 16px 邊距、44px 密碼顯示按鈕、84px 輸入框右側留白）
- [x] `Tests/Unit/mobile-navigation.test.mjs`（5 個測試：`isMoreSection`、開啟狀態同步、backdrop 關閉＋焦點還原、面板內點擊不關閉、關閉鈕）
- [x] **視覺驗證已補上**：用 headless Chrome（`chrome.exe --headless=new --remote-debugging-port`）+ Node 內建 `WebSocket` 直接講 Chrome DevTools Protocol 截圖（不需安裝 Playwright/Puppeteer），對六個頁面在 320×568／375×667／768×1024／1024×768 四組視窗尺寸下截圖，額外驗證 More sheet、新增紀錄對話框、Calendar 週視圖，含長分類名稱與長標籤名稱的真實資料。**沒有發現任何視覺缺陷**（無水平溢出、底部導覽/側欄正確切換、長文字正確換行、對話框動作按鈕不被遮住）。細節見 [`Docs/Rwd.md`](Rwd.md) 的「視覺驗證」一節
- 詳細實作說明見 [`Docs/Rwd.md`](Rwd.md)

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
- [x] Unit Tests：Calendar View 的跨午夜切段與重疊分欄純函式（`Tests/Unit/calendar-state.test.mjs`，5 個測試）、`timezone.mjs` 新增的日期運算函式（`Tests/Unit/timezone.test.mjs`，新增 4 個測試），細節見 [`Docs/CalendarView.md`](CalendarView.md)
- [x] Unit/Integration Tests：CSV export（`CsvWriterTests` 3 個：BOM/CRLF/escape/公式中和/空資料；`CsvExportServiceTests` 5 個：完整排序、跨午夜完整一筆、201 筆無分頁上限、跨使用者隔離＋空結果、New York 春季 DST 時長），`Tests/TimeEntryFlow.Tests` 目前共 105 個測試，細節見 [`Docs/CsvExport.md`](CsvExport.md)
- [x] Unit Tests：Timer 卡片的經過時間格式化純函式（`Tests/Unit/timer-state.test.mjs`，3 個測試，含超過 24 小時不繞回 0 的迴歸案例）
- [x] Unit Tests：共用 More sheet 控制器（`Tests/Unit/mobile-navigation.test.mjs`，5 個測試：`isMoreSection` 分類、開啟時 `aria-expanded`/focus、backdrop 關閉＋焦點還原、面板內點擊不關閉、關閉鈕）。`Tests/Unit/` 目前共 27 個 Node 測試（`node --test Tests/Unit/*.test.mjs`）

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
- **釐清「Timer 卡片時區」的既有 TODO 項目其實是誤判**：`timer.js` 的即時顯示只算經過秒數，跟日曆日期/帳號時區無關，過去文件把它跟 History List 的時區 bug 混為一談。順手修好一個真的存在、跟這次調查相關的小 bug：`formatClock` 借道 `Date`/`toISOString` 換算，計時超過 24 小時會繞回 `00:00:00`；已抽成 `wwwroot/js/timer-state.mjs` 改用純整數運算修正，新增 `Tests/Unit/timer-state.test.mjs`。
- RWD 切版（第 14 項）：三種版面帶（桌面固定側欄／平板窄桌面／手機，皆以 720px、900px 為門檻，沿用既有 `dashboard.css` 斷點，不新增第四種）、共用的 `_MobileNavigation.cshtml` 局部檢視 + `mobile-navigation.mjs`/`.js` 讓 Category/Tag/Settings 首次擁有行動裝置底部導覽（先前是死路）、原生 `<dialog>` 實作的「更多」底部彈出選單（無 polyfill、無第三方選單套件）、`dashboard.css` 統一 shell 規則（44px 觸控目標、安全區留白、`prefers-reduced-motion`）、順手修正 Settings 頁面「儲存」按鈕因未載入 `category-tag.css` 而完全無樣式的既有問題，詳見 [`Docs/Rwd.md`](Rwd.md)

## 下一步建議優先順序
1. 200% 瀏覽器縮放、鍵盤 Tab 順序、螢幕閱讀器 focus 走向這幾項 RWD 驗收矩陣要求的細節，截圖驗證看不出來，需要真人操作瀏覽器才能補（見 [`Docs/Rwd.md`](Rwd.md) 的「尚未涵蓋的部分」）
2. 全域例外處理與其餘 `Infrastructure/Logging` 事件（DB 錯誤、timer transaction failure、CSV export failure）——CSV export 本身已完成，但它失敗時的 log 事件還沒補（沿用專案既有慣例，跟其餘 logging 基礎設施一起做）
3. AGENTS.md Implementation Order 的第 1–14 項已全數完成，剩下第 15（錯誤處理/日誌）、16（部署）
