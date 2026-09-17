# Report

依照 [`Docs/superpowers/specs/2026-09-17-report-design.md`](superpowers/specs/2026-09-17-report-design.md) 與 [`Docs/superpowers/plans/2026-09-17-report.md`](superpowers/plans/2026-09-17-report.md) 完整實作，對照 `AGENTS.md` 的第 11 項。設計文件本身沒有 TBD。

## 設計思路

`ReportService` 是 `TimeAggregationService` 的純消費者，職責只有「解析 preset/custom range → 呼叫 `AggregateAsync` → 取 `CategoryTotals` 或 `TagTotals` 組成回應」，不重寫任何時區轉換、interval intersection 或 Category/Tag 分桶邏輯——這是 `AGENTS.md`「Dashboard 與 Report 不得各自實作聚合邏輯」規則的直接體現，也是 `Docs/Dashboard.md` 已經建立的模式。

Range 解析規則與 `DashboardService` 幾乎相同（custom 需要 `startDate`+`endDate` 且不可與 preset 同時給、preset 只接受 `today`/`week`/`month`、custom 的 `endDate < startDate` 回 `INVALID_DATE_RANGE`），唯一差異是**預設值**：Dashboard 在什麼都不給時回 400 `INVALID_RANGE_PRESET`，Report 則依規格書「預設 month」在什麼都不給時直接視為 `preset=month`。這個差異被封裝在 `ReportService` 私有的 `ResolveRangeAsync`（一個 `GetCategoryAsync`/`GetTagAsync` 共用的 `readonly record struct RangeResolution` 回傳型別），避免兩個 public 方法各自重複一份互斥驗證邏輯。

Category 回應帶 `TotalSeconds`（前端圓餅圖以此為分母算百分比）；Tag 回應也帶 `TotalSeconds`，但只當作參考資訊，**絕對不能被前端拿來算百分比**——因為 Tag 是可重複累計的（一筆多 Tag 紀錄的全時長會算進每個 Tag），總和可能超過 `TotalSeconds`，這正是 `AGENTS.md`「不要用圓餅圖表示 Tag」的理由，`Docs/Report.md` 沿用同一份 `TagTotal`/`CategoryTotal` DTO（`Models/Responses/TimeAggregationResult.cs`），前端渲染時刻意分成兩條路徑（`renderCategory` 算百分比、`renderTag` 只算相對最大值的長條寬度）。

## 整體流程

1. `GET /Report` → `ReportController.Index()`（`[Authorize]`）回傳頁面殼。
2. `report.js` 載入後立刻以 `preset=month` 平行呼叫 `GET /api/reports/category` 與 `GET /api/reports/tag`（`Promise.all`），用遞增的 `requestId` 忽略被使用者快速切換 range 蓋過的舊回應。
3. 使用者點 Today/Week/Month 或套用 Custom range → 兩個端點以同一組 query string 重新平行呼叫，Category donut/legend 與 Tag bar 同步更新。
4. 兩個 API 端點內部都是「`CurrentUserService.GetRequiredUserId()` → `ReportService.GetCategoryAsync`/`GetTagAsync` → `TimeAggregationService.GetRangeForPreset`/`AggregateAsync`」，全程只查一次 `TimeEntries`（`AggregateAsync` 內部一次查詢同時算出 Category 與 Tag 兩種 totals，Report 只是分別只取用其中一半）。

## 檔案清單與內容

- `Models/Requests/ReportRequest.cs`：`Preset`/`StartDate`/`EndDate`，與 `DashboardRequest` 同構。
- `Models/Responses/ReportResponse.cs`：`CategoryReportResponse(StartDate, EndDateInclusive, TimeZoneId, TotalSeconds, CategoryTotals)`、`TagReportResponse(StartDate, EndDateInclusive, TimeZoneId, TotalSeconds, TagTotals)`，直接重用 `Models/Responses/TimeAggregationResult.cs` 裡既有的 `CategoryTotal`/`TagTotal` record，不重新定義一份。
- `Services/ReportService.cs`：`CategoryReportResult`/`TagReportResult`（標準 `XResult` pattern）、`GetCategoryAsync`/`GetTagAsync`、私有 `ResolveRangeAsync`/`RangeResolution`/`TryParsePreset`。
- `Controllers/ReportController.cs`：`[Authorize]`，`Index()` 只回傳 View。
- `Controllers/Api/ReportApiController.cs`：`[Authorize]`、`Route("api/reports")`，`GET category`/`GET tag`，`MapFailure` 把 `INVALID_DATE_RANGE`/`INVALID_RANGE_PRESET` 對應到 400，其餘 500。
- `Program.cs`：新增 `builder.Services.AddScoped<ReportService>();`。
- `Views/Report/Index.cshtml`：頁面殼，側欄/`mobile-nav`「報表」連結改成 `asp-controller="Report"`（`aria-current="page"`），range 控制項與 Dashboard 同構（Today/Week/Month/Custom），初始預設按鈕是「本月」（對齊後端預設）。兩個 `dashboard-panel`（Category donut+legend、Tag bar chart）並排，重用 `dashboard.css` 既有的 `.donut-layout`/`.category-donut`/`.chart-legend`/`.tag-chart`/`.tag-row` 元件，沒有新增圖表函式庫。
- `wwwroot/css/report.css`：只新增 `.report-grid`（桌面雙欄、900px 以下單欄，斷點對齊 `dashboard.css` 既有的側欄收合斷點，避免 768–900px 之間出現「已經是手機導覽但圖表還沒疊起來」的不協調狀態）。
- `wwwroot/js/report.js`：`splitDate`/`formatDate*`/`formatRangeLabel` 複製自 `dashboard.js`（沒有抽成共用模組——延續既有慣例，`dashboard-state.mjs` 只保留與 DOM 無關的純函式，其餘頁面各自持有自己的格式化 helper）。`renderCategory` 用 conic-gradient 畫圓餅、legend 顯示色點/名稱/時長**與百分比**（`Math.round(item.durationSeconds / response.totalSeconds * 100)`，只有 `totalSeconds > 0` 才計算，避免除以零）。`renderTag` 用 `.tag-row` 長條，寬度只相對於當期最大值，**不顯示百分比**。`runRequest` 用 `Promise.all` 平行呼叫兩端點、`requestId` 忽略舊回應，任一端點失敗就顯示第一個可用的錯誤訊息。所有名稱一律用 `textContent`/`createTextNode`，沒有把使用者輸入字串接進 `innerHTML`。
- `Tests/TimeEntryFlow.Tests/Integration/ReportServiceTests.cs`：14 個測試，覆蓋「不給任何參數時預設本月」、三種 preset 都能用、custom range（含只給一半視為 `INVALID_RANGE_PRESET`）、preset 與 custom 同給、無效 preset、無效日期範圍、未分類色彩（`#a6adb7`，與 `TimeAggregationService` 共用同一個常數）、Tag additive 加總與排序、空資料、跨使用者隔離、Category 依時長降冪排序。

## 尚未涵蓋的部分

- 沒有 Calendar/Dashboard 那種「相鄰比較期」概念——規格書明確排除，Report 只顯示單一期間的分布，不算變化量。
- 沒有新增任何第三方圖表函式庫，圓餅圖與長條圖都是既有 CSS（conic-gradient / flex 長條）重用，符合規格「不在範圍」。
- CSV export（`AGENTS.md` 第 13 項）仍是空殼，Report 頁面沒有匯出按鈕。
- 手動驗證：本機啟動 `dotnet run --no-build`（port 5180），用 curl 走完整流程——註冊 → 建立 2 個 Category、3 筆 TimeEntry（含未分類、多 Tag 疊加）→ `GET /Report`（200，`report.js`/`report.css` 正確接線）→ 不給參數（預設本月，含未分類與 Tag 疊加加總正確）→ 各 preset → custom range → 各種錯誤組合（無效 preset、無效日期範圍、preset 與 custom 同給）全部回正確的 400/errorCode → 確認 Dashboard/History/Category/Tag/Settings 側欄與行動導覽的「報表」連結都已指向 `/Report`。這個 session 沒有可用的瀏覽器自動化工具，桌面雙欄／900px 以下單欄堆疊的實際視覺效果沒有在真的瀏覽器裡驗證過，接手後建議補上。驗證用的測試資料（Category、TimeEntry、Tag、帳號）已於驗證後刪除。
