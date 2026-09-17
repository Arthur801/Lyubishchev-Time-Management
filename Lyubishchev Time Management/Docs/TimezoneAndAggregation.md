# Timezone Settings 與 TimeAggregationService 實作說明

依照 [`Docs/superpowers/specs/2026-09-17-timezone-and-aggregation-design.md`](superpowers/specs/2026-09-17-timezone-and-aggregation-design.md)（以及對應的 [`Docs/superpowers/plans/2026-09-17-timezone-settings.md`](superpowers/plans/2026-09-17-timezone-settings.md)、[`Docs/superpowers/plans/2026-09-17-time-aggregation-service.md`](superpowers/plans/2026-09-17-time-aggregation-service.md)）完整實作，設計文件本身沒有 TBD。這裡只記錄實作時的具體事實。

---

## 設計思路

- `TimeZoneCatalog`（`Infrastructure/Time/`）是唯一的時區白名單、顯示名稱與 `TimeZoneInfo` 解析入口，`UserSettingsService`（寫入）與 `TimeAggregationService`（讀取範圍）共用同一份，Razor/JS 不自行複製清單。`ResolveOrUtc` 對不在白名單的 ID 一律降級為 `UTC`——這只保護舊資料/手動修復/未來部署錯誤，正常的設定更新流程一定先過 `IsSupported` 驗證，不會走到這個 fallback。
- `UserSettingsService` 只改 `User.TimeZoneId`/`UpdatedAtUtc`，不碰任何 `TimeEntry` 的 UTC 欄位——時區只是「顯示/查詢時怎麼切日期」的設定，不是資料本身。
- `TimeAggregationService` 是唯讀服務：EF 查詢只做 `UserId` 擁有權過濾與 `StartTimeUtc < rangeEndUtc && EndTimeUtc > rangeStartUtc` 的 overlap 過濾，所有時區轉換、DST 邊界、逐日切分、Category/Tag 分桶都在記憶體內完成（`TimeZoneInfo`/`DateOnly` 無法翻譯進 EF query）。
- 本地日期轉 UTC 一律用半開區間 `[rangeStartUtc, rangeEndUtc)`；跨午夜、DST 缺失/重複小時都在同一個 `ConvertLocalMidnightToUtc` helper 內處理：無效本地時間（spring-forward 缺口）往前找第一個有效時間；歧義本地時間（fall-back 重複）固定選較早的 UTC 瞬間（用 `GetAmbiguousTimeOffsets` 裡較大的 offset，也就是 DST 側），確保連續日期範圍前後不重疊、不留縫。

## 整體流程

**Timezone settings：**

1. `GET /api/settings/timezone` → 回傳使用者目前的 `TimeZoneId` 與完整選項清單。
2. `PATCH /api/settings/timezone`（body `{ "timeZoneId": "Asia/Taipei" }`）→ `UserSettingsService.UpdateAsync` 驗證白名單（缺少/空白/不支援都回同一個 `INVALID_TIME_ZONE`）→ 查無使用者回 `USER_NOT_FOUND` → 合法時只更新 `TimeZoneId`/`UpdatedAtUtc`（`IClock.UtcNow`）→ 回傳新設定。重複選擇目前時區仍視為成功、仍更新 `UpdatedAtUtc`。
3. `/Settings` 頁面：載入時 GET 選項並選中目前值；送出時 PATCH。`Intl.DateTimeFormat().resolvedOptions().timeZone` 只用來顯示「瀏覽器偵測到的時區」提示文字，不會自動覆寫或送出。

**TimeAggregationService：**

1. `GetRangeForPreset(DateRangePreset.Today|ThisWeek|ThisMonth, timeZoneId)`：用 `catalog.ResolveOrUtc` 把 `IClock.UtcNow` 轉成使用者當地日期，算出 `LocalDateRange`。週從星期日開始（`today.AddDays(-(int)today.DayOfWeek)`），月從當月 1 日到當月最後一天。Custom range 由呼叫端直接建構 `LocalDateRange`，不經過這個方法。
2. `AggregateAsync(userId, range, timeZoneId, ct)`：`range.EndDateInclusive < range.StartDate` 丟 `ArgumentException`；否則把 `StartDate 00:00` 與 `(EndDateInclusive + 1) 00:00` 轉成 UTC 半開邊界，查出 overlap 的 TimeEntries（含 `Category`、`TimeEntryTags.Tag`），逐筆算 `overlapStart/overlapEnd` 交集時長，同時：
   - 累加到 `TotalSeconds`。
   - 依本地日期切分累加到 `DailyTotals`（範圍內每一天都預先放 0，即使沒有紀錄也會輸出）。
   - 依 `CategoryId` 累加到 `CategoryTotals`（整段交集時長算一次，無 Category 的另外累加進「未分類」桶，總和等於 `TotalSeconds`）。
   - 依每個關聯 Tag 累加到 `TagTotals`（整段交集時長加到每一個 Tag，總和可能大於 `TotalSeconds`，這是預期行為）。
   - 排序皆為時長由大到小、同時長時名稱以 `OrdinalIgnoreCase` 排序。

## 開發過程中抓到的問題

**`Dictionary<TKey, TValue>` 不接受 `null` key，即使 `TKey` 是可為 null 的值型別（`ulong?`）。** 第一版把 Category 統計做成 `Dictionary<ulong?, (...)>`，用 `null` key 代表「未分類」桶。編譯器有丟出 `CS8714`（`notnull` 限制式警告），一開始以為只是型別系統對 nullable value type 過度嚴格的假警報而直接 `#pragma warning disable` 蓋掉——但實際跑測試立刻在 `Dictionary<TKey,TValue>.FindValue` 炸出 `ArgumentNullException`：CLR 的 `Dictionary` 執行期就是禁止 `null` key，跟 `TKey` 是不是 nullable 值型別無關。**教訓：這個警告不是雜訊，遇到它應該先假設「執行期真的會炸」再去查證，不要急著蓋掉。** 修正方式：`categoryTotals` 改回 `Dictionary<ulong, (...)>`（只存有 Category 的桶），未分類另外用一個獨立的 `long uncategorizedSeconds` 累加器追蹤，組裝最終結果時才把它併入輸出清單（且只在 `> 0` 時才輸出，跟 `CategoryTotals` 只列出實際出現過的桶這個既有行為一致）。

## 檔案清單與內容

### `Infrastructure/Time/TimeZoneCatalog.cs`

`TimeZoneOption(string Id, string DisplayName)` record；`TimeZoneCatalog.Options` 是 13 個常用 IANA 時區的靜態白名單（繁體中文顯示名稱）；`IsSupported`/`ResolveOrUtc`。

### `Models/Requests/UpdateTimezoneRequest.cs`

只有 `TimeZoneId` 一個欄位，刻意不加 `[Required]` 等 annotation——缺少/空白/不支援都要走同一個 `INVALID_TIME_ZONE` 錯誤碼，所以驗證完全交給 `UserSettingsService`，不要讓 ModelState 驗證跟 service 驗證分裂成兩種錯誤格式。

### `Models/Responses/TimezoneSettingsResponse.cs`、`Models/Responses/TimeAggregationResult.cs`

前者是 `(string TimeZoneId, IReadOnlyList<TimeZoneOption> Options)`。後者定義 `DateRangePreset` enum、`LocalDateRange`、`DailyTotal`、`CategoryTotal`（`CategoryId` 可為 `null`）、`TagTotal`、`TimeAggregationResult`。

### `Services/UserSettingsService.cs`、`Services/TimeAggregationService.cs`

前者提供 `GetAsync`/`UpdateAsync`（回傳 `UserSettingsResult`，沿用專案既有的 `Result` record 慣例）。後者提供 `GetRangeForPreset`/`AggregateAsync`；因為沒有會員可預期的失敗路徑（呼叫端保證合法輸入），直接回傳 DTO，不包 `XResult`。

### `Controllers/SettingsController.cs`、`Controllers/Api/SettingsApiController.cs`

前者只回傳 `/Settings` 頁面（`[Authorize]`，不碰 `AppDbContext`）。後者是 `GET`/`PATCH /api/settings/timezone`（`[Authorize]`，PATCH 額外 `[ValidateAntiForgeryToken]`），`ErrorCode` → HTTP 狀態碼映射沿用既有 `MapFailure` switch 慣例（`INVALID_TIME_ZONE` → 400，`USER_NOT_FOUND` → 404）。

### `Views/Settings/Index.cshtml`、`wwwroot/js/settings.js`、`wwwroot/css/settings.css`

比照 `Views/Category/Index.cshtml` 的側欄/頁面殼慣例；`settings.js` 沿用既有 `callApi()`（CSRF header、Problem Details `detail` 呈現錯誤）。`Intl.DateTimeFormat().resolvedOptions().timeZone` 只寫進提示文字，不自動送出。

### `Program.cs`

新增 `AddSingleton<TimeZoneCatalog>()`（純靜態白名單，無狀態，適合 Singleton）、`AddScoped<UserSettingsService>()`、`AddScoped<TimeAggregationService>()`。

### `Tests/TimeEntryFlow.Tests/Unit/TimeZoneCatalogTests.cs`、`Tests/TimeEntryFlow.Tests/Integration/{UserSettingsServiceTests,TimeAggregationServiceTests}.cs`（新增）

`TimeZoneCatalogTests` 是純單元測試（不碰 DB），放進新的 `Unit/` 資料夾，比照 `AuthFlow.Tests` 既有的 `Unit`/`Integration` 分法。`UserSettingsServiceTests`／`TimeAggregationServiceTests` 沿用 `TestDatabase` SQLite shared in-memory fixture，涵蓋：白名單校驗、跨使用者隔離、改時區不動 `TimeEntry` UTC 值、跨午夜切分、零紀錄日補 0、範圍外資料排除、未分類 `#a6adb7` 桶、Category 總和等於總時長、Tag 加總可超過總時長、重疊紀錄相加、`America/New_York` 春季缺一小時／秋季多一小時整天時長正確（用兩個本地午夜的 UTC 差直接驗證，不特別造出剛好落在缺口/歧義區間裡的紀錄，因為美國的轉換時刻是凌晨 2 點而非午夜）。`Tests/TimeEntryFlow.Tests` 目前共 70 個測試全過。

## 手動驗證

本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 走完整流程：註冊 → `GET /Settings`（200）→ `GET /api/settings/timezone`（預設 `Asia/Taipei` + 13 個選項）→ `PATCH` 改成 `America/New_York`（200，回傳新設定）→ 再次 `GET` 確認持久化 → `PATCH` 不支援的時區 ID（400 `INVALID_TIME_ZONE`）。驗證用的測試帳號已於驗證後從資料庫刪除。

## 尚未涵蓋的部分

- **Dashboard/Report 尚未消費 `TimeAggregationService`**：目前 Dashboard 的統計卡片/圖表仍是前端 mock data（`dashboard-state.mjs`），串接是第 10、11 項的責任。
- **Dashboard 計時器與 History List 的日期範圍計算仍是瀏覽器本地時區的簡化做法**，尚未改用使用者設定的時區——這是設計文件明確排除的範圍（「不在範圍：Dashboard／Report 真實資料 API、Calendar View、CSV export 與既有 TimeEntry CRUD 表單的前端改造」），留給消費此服務的後續項目一併處理。
- **其他頁面側欄的「設定」連結仍是 `href="#"` placeholder**，沒有改指向新的 `/Settings` 路由——這次刻意把改動範圍鎖在設計文件列出的檔案清單內，沒有動其他既有頁面。
- **Calendar View、CSV export 尚未使用時區設定**，屬於第 8、13 項的範圍。
