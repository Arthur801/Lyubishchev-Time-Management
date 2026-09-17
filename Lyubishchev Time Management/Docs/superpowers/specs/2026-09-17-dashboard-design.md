# Dashboard 設計

**日期：** 2026-09-17

**範圍：** Dashboard 真實資料 API、DashboardService、日期範圍與比較期、既有 Dashboard mock 前端替換。
**不在範圍：** TimeAggregationService 的交集／時區演算法、Timezone Settings、Timer Start/Stop、Report、Calendar、CSV export。

## 目標

以使用者帳號時區顯示 Today、ThisWeek、ThisMonth 或 custom date range 的真實時間總覽，取代 `dashboard-state.mjs` mock data，同時保留現有 Timer 卡片及 Desktop/Mobile 版面。

## 架構

`DashboardService` 只組裝資料：它從目前 User 的 `TimeZoneId` 取得時區，呼叫 `TimeAggregationService` 產生本期與比較期統計，再查詢最近完成的五筆 TimeEntry。它不得自行重寫 UTC 範圍、跨午夜切段、Category／Tag aggregation 或 DST 邏輯。

`GET /api/dashboard` 為唯一資料端點，受 `[Authorize]` 保護。接受 `preset=today|week|month`，或同時提供 `startDate`／`endDate` 的 custom range。日期格式為 `yyyy-MM-dd`，依帳號時區解讀；`endDate < startDate` 回傳 400 `INVALID_DATE_RANGE`，未知 preset 回傳 400 `INVALID_RANGE_PRESET`。API 不接受 UserId。

## 範圍與比較期

本期使用 `TimeAggregationService` 的 local date range。比較期永遠是本期前方、相鄰且等長的 local date range：Today 對 Yesterday、ThisWeek 對前一週、ThisMonth 對前一個曆月、custom 對前一段同日數區間。比較只產生 `ComparisonSeconds` 與 `ChangeSeconds`，不混入本期的圖表資料。

## Response 與畫面對映

`DashboardResponse` 包含：本期 local range、時區 ID、`TotalSeconds`、`ComparisonSeconds`、`ChangeSeconds`、daily/category/tag totals、`RecentEntries`。Daily totals 驅動每日趨勢；Category totals 驅動 donut 與 legend，含 `Uncategorized`；Tag totals 驅動長條圖，並保留其可加總超過總時長的語意。Recent entries 依 `EndTimeUtc` 再以 Id 由新到舊排序，最多五筆，使用既有 `TimeEntryResponse` 所需的名稱、分類色彩、Tag 與時長欄位。

空資料時所有總時長是 0、趨勢仍顯示範圍日別的零值、Category／Tag／最近活動顯示空狀態；前端不渲染 NaN、無效 conic-gradient 或假的 top category/tag。

## 前端

`dashboard.js` 取代 mock snapshot 的 fetch／render；`dashboard-state.mjs` 的 mock data 及 `addCompletedEntry` 移除。既有 range buttons、custom form、圖表容器與可存取的 summary 元素維持。每次範圍改變會取消或忽略較舊請求，只渲染最後一次成功回應。Timer 仍由 `timer.js` 單獨呼叫 `/api/timer`，Dashboard API 不接收 Timer state。

## 測試與驗收

服務測試覆蓋所有 preset、custom、相鄰比較期、空資料、跨使用者隔離、最近五筆排序與 Tag additive totals。前端驗收確認所有 range 操作、錯誤訊息、空資料、資料載入失敗、桌面和 320px 行動布局；Timer Start/Stop 既有流程不能受影響。
