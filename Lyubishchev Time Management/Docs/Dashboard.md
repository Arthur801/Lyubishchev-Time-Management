# Dashboard 實作說明

依照 [`Docs/superpowers/specs/2026-09-17-dashboard-design.md`](superpowers/specs/2026-09-17-dashboard-design.md)（對應 [`Docs/superpowers/plans/2026-09-17-dashboard.md`](superpowers/plans/2026-09-17-dashboard.md)）完整實作，設計文件本身沒有 TBD。這裡只記錄實作時的具體事實。

---

## 設計思路

- `DashboardService` 只組裝資料，不重寫任何時區轉換／交集／DST 邏輯——本期與比較期都直接呼叫 [`TimeAggregationService`](TimezoneAndAggregation.md)（`GetRangeForPreset`/`AggregateAsync`），最近五筆活動用一個獨立、不受選定範圍限制的查詢。
- `GET /api/dashboard` 是唯一資料端點，preset（`today`/`week`/`month`）與 custom range（`startDate`+`endDate`）互斥：必須恰好符合其中一種合法組合，其餘組合（兩者都給、都不給、preset 未知、custom 只給一半）一律 400 `INVALID_RANGE_PRESET`；custom 範圍合法但 `endDate < startDate` 才是 400 `INVALID_DATE_RANGE`。
- 比較期永遠是本期前方、相鄰、不重疊的 local range：Today/Week/Custom 用「等長」（day count 相同）推算，因為這三種天生就是固定或使用者自訂的天數；Month 則改用「前一個曆月」，因為月份長度不固定（例如 9 月 30 天對 8 月 31 天），使用者比較的是行事曆月份而不是天數。這是設計文件裡兩種措辭（「相鄰且等長」vs「前一個曆月」）唯一能同時成立的解讀方式，已在 `DashboardServiceTests` 用不同長度的月份組合驗證過。

## 整體流程

1. `DashboardService.GetAsync` 先取得目前使用者的 `TimeZoneId`。
2. 依 `preset`/`startDate`/`endDate` 判斷模式（`Today`/`Week`/`Month`/`Custom`），算出本期 `LocalDateRange`（preset 模式呼叫 `TimeAggregationService.GetRangeForPreset`，custom 模式直接用兩個日期建構，並檢查 `endDate >= startDate`）。
3. 依模式算出相鄰、不重疊的比較期 `LocalDateRange`（規則見上）。
4. 呼叫兩次 `TimeAggregationService.AggregateAsync`（本期、比較期），本期取完整的 daily/Category/Tag totals，比較期只取 `TotalSeconds`。
5. 另外查詢 `TimeEntries.Where(e => e.UserId == userId).OrderByDescending(EndTimeUtc).ThenByDescending((long)Id).Take(5)`（含 Category、Tags）——這個查詢刻意不套用本期範圍過濾，永遠回傳全域最近五筆，與選取的日期範圍無關。
6. 組成 `DashboardResponse` 回傳：本期 `StartDate`/`EndDateInclusive`、`TimeZoneId`、`TotalSeconds`、`ComparisonSeconds`、`ChangeSeconds`、`DailyTotals`/`CategoryTotals`/`TagTotals`（沿用 `TimeAggregationResult` 的 DTO，不重新定義）、`RecentEntries`（沿用既有 `TimeEntryResponse`）。

`dashboard.js` 對每次 range 切換都發一個新的 fetch，用遞增的 `requestId` 忽略較舊、較晚回來的回應；preset 按鈕與「套用」自訂範圍共用同一個 `runRequest` 路徑。空資料時：總時長顯示 `0 小時 0 分`、Category donut 不產生 conic-gradient（改用中性底色）、Category/Tag/最近活動各自顯示文字空狀態，不會出現 `NaN` 或假資料。

## 檔案清單與內容

### `Models/Requests/DashboardRequest.cs`（新增）

`Preset`/`StartDate`/`EndDate` 皆為 nullable；`StartDate`/`EndDate` 用 `DateOnly?`，ASP.NET Core 原生支援從 query string 綁定，不需要自訂 model binder。

### `Models/Responses/DashboardResponse.cs`

直接複用 `Models/Responses/TimeAggregationResult.cs` 裡的 `DailyTotal`/`CategoryTotal`/`TagTotal`，以及既有的 `TimeEntryResponse`，不重複定義 DTO。

### `Services/DashboardService.cs`

`DashboardResult`（沿用既有 `XResult` record 慣例）；內部 `RangeMode` enum 區分 Today/Week/Month/Custom 以決定比較期算法；`GetComparisonRange` 是唯一的比較期邏輯集中點。

### `Controllers/Api/DashboardApiController.cs`

`GET /api/dashboard`（`[Authorize]`，讀取端點不需要 CSRF）；`MapFailure` 沿用既有 switch 慣例，`INVALID_RANGE_PRESET`/`INVALID_DATE_RANGE` 皆映射 400。

### `Program.cs`

新增 `AddScoped<DashboardService>()`。

### `wwwroot/js/dashboard-state.mjs`

移除所有 mock snapshot 資料與 `getPresetSnapshot`/`addCompletedEntry`，只保留兩個純函式：`formatDuration`（改吃秒數，原本吃分鐘——因為後端回傳的是 `TotalSeconds`/`DurationSeconds`，直接用秒可以少一次轉換）與 `isValidDateRange`（不變）。保留這個檔案（沒有整個刪除）是因為 `Tests/Unit/dashboard-frontend.test.mjs` 需要一個不依賴 `document` 的純邏輯模組才能在 Node 環境下測試。

### `wwwroot/js/dashboard.js`

改為對 `/api/dashboard` 發真實 fetch；range 按鈕與自訂範圍套用共用 `runRequest`，用遞增 `requestId` 蓋掉較舊的回應。渲染 Category legend／Recent list 的名稱一律用 `textContent`/DOM node，不把使用者輸入字串接進 `innerHTML`（比照 `category.js`/`tag.js` 既有慣例）。最近活動的時間改用 `Intl.DateTimeFormat` 搭配回應裡的 `timeZoneId` 格式化，不用瀏覽器本地時區——這是當時 Dashboard 程式碼裡第一個改用帳號時區顯示時間的地方（History List 後續 session 也已經改用帳號時區，見「尚未涵蓋的部分」的更新註記）。Timer 卡片完全未觸碰，仍由 `timer.js` 獨立呼叫 `/api/timer`。

### `Views/Dashboard/Index.cshtml`

拿掉硬編碼的 mock 值（`#range-label`、`#total-duration`、`#top-category`、`#donut-total`、`#range-start`/`#range-end` 的 `value` 屬性等），改成載入中/空狀態的預留字串，交由 `dashboard.js` 首次 fetch 完成後覆寫。「查看全部」連結從 `href="#"` 改成 `asp-controller="TimeEntry" asp-action="Index"`。

### `Tests/TimeEntryFlow.Tests/Integration/DashboardServiceTests.cs`（新增）

沿用 `TestDatabase` SQLite fixture，涵蓋：Today/Week/Month/Custom 各自的比較期計算（含不同長度月份）、custom range 顛倒日期回 400、未知/缺少 preset 回 400、preset 與 custom 同時給回 400、空資料歸零、最近五筆排序、跨使用者隔離、Category/Tag totals 透傳（未分類桶、Tag additive）。`Tests/TimeEntryFlow.Tests` 目前共 83 個測試全過。

### `Tests/Unit/dashboard-frontend.test.mjs`

移除對已刪除之 mock 函式（`getPresetSnapshot`/`addCompletedEntry`）的測試；`formatDuration` 測試改用秒數輸入。**順手修正一個既有的測試錯誤**：原本最後一個測試對 `Views/Shared/_Layout.cshtml`（共用版面）斷言含有 `dashboard.css`/`dashboard.js`字樣，但這兩個資源其實是由 `Views/Dashboard/Index.cshtml` 透過 `@section Styles`/`@section Scripts` 載入，共用版面本身只有 `@await RenderSectionAsync(...)`，從未直接出現這兩個檔名——這個斷言從一開始就是錯的（用 `node --test` 實際執行原始測試檔驗證過會失敗）。已改成同時讀取兩個檔案：共用版面斷言「不含」這兩個字樣（維持通用）、Dashboard 頁面斷言「有」。

## 尚未涵蓋的部分

- ~~Timer 卡片與 History List 仍是瀏覽器本地時區的簡化做法~~ **（後續 session 更新）**：History List（`time-entry.js`）已經改用帳號時區，見 [`Docs/HANDOFF.md`](HANDOFF.md)「這個 session 中發現並修好的重要地雷」一節。Timer 卡片經查證後**不需要改**——它的即時顯示只算經過秒數（`Date.now() - startedAtUtc`），從來不涉及日曆日期/時區計算，過去這裡把它跟 History List 的 bug 混為一談是誤判，細節同見上述 HANDOFF 章節。
- ~~Report（第 11 項）仍未開始~~ **（後續 session 更新，已完成）**：見 [`Docs/Report.md`](Report.md)，延續了這裡預期的「只組裝、不重寫聚合邏輯」模式。
- ~~側欄「報表」「設定」連結仍是 `href="#"` placeholder~~ **（後續 session 更新，已完成）**：全站側欄與行動導覽的「報表」「設定」連結都已接上 `/Report`/`/Settings`，見 [`Docs/Report.md`](Report.md) 與 [`Docs/Rwd.md`](Rwd.md)。
- **前端沒有自動化的互動測試**（例如實際點擊 range 按鈕、確認 fetch 呼叫與畫面更新），只有 `dashboard-frontend.test.mjs` 對純函式與靜態標記的驗證；瀏覽器互動驗證是靠本機手動 curl 對 `/api/dashboard` 的驗收（見下方）＋人工檢查 HTML 結構，這個 session 的環境沒有可用的瀏覽器自動化工具，實際的桌面／320px 版面與互動流程建議接手後在真的瀏覽器裡再看一次。

## 手動驗證

本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 走完整流程：註冊 → 建立一筆 Category、一筆帶 Tag 的 TimeEntry（刻意跨 UTC 午夜但落在同一個台北本地日內）→ `GET /api/dashboard?preset=today`、`?preset=week`、`?preset=month`、自訂範圍、無效 preset（400 `INVALID_RANGE_PRESET`）、`endDate < startDate`（400 `INVALID_DATE_RANGE`）、preset 與 custom 同時提供（400 `INVALID_RANGE_PRESET`）皆符合預期 → `GET /Dashboard` 確認頁面載入且 `dashboard.js`/`dashboard.css`/「查看全部」連結正確接線。驗證用的測試資料（Category、TimeEntry、帳號）已於驗證後刪除。
