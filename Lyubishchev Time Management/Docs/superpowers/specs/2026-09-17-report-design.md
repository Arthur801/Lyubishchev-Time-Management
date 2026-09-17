# Report 設計

**日期：** 2026-09-17

**範圍：** `/Report` 頁面、Category/Tag report API、ReportService、日期範圍與 RWD 圖表呈現。
**不在範圍：** TimeAggregationService、Timezone Settings、Dashboard、Calendar、CSV export、圖表套件、可編輯報表或儲存的篩選條件。

## 目標

讓使用者以帳號時區檢視本月預設或指定本地日期範圍內的 Category 與 Tag 時間分布。Category 用圓餅圖表示占總追蹤時間的比例；Tag 用由大到小的長條圖表示可加總時長。

## 架構與端點

`ReportService` 讀取目前 User 的 `TimeZoneId`，將選擇的 preset/custom date range 交給 `TimeAggregationService`。它不得自行計算 UTC 日界、DST、interval intersection、Category 或 Tag totals。

`GET /api/reports/category` 與 `GET /api/reports/tag` 都接受 `preset=today|week|month` 或同時提供 `startDate`、`endDate`；預設 `month`。日期採 `yyyy-MM-dd`，以帳號時區解讀。兩端點均受 `[Authorize]` 保護、從 JWT 取得 UserId；無效日期範圍回 400 `INVALID_DATE_RANGE`，未知 preset 回 400 `INVALID_RANGE_PRESET`。

Category response 回傳 local range、時區、總秒數和 Category totals；Tag response 回傳同一範圍、時區和 Tag totals。兩者不回傳 UserId、Entity navigation 或原始完整 TimeEntry。

## 圖表與資料語意

- Category totals 含 `Uncategorized`，色彩為 `#a6adb7`；圓餅百分比以本期 `TotalSeconds` 為分母，所有 Category total 之和等於它。
- Tag totals 依秒數降冪、名稱不分大小寫作 tie-break；一筆多 Tag 紀錄會把全時長計入每個 Tag，故長條圖不顯示「佔總時間百分比」，且 title 明示「可重複累計」。
- 沒有紀錄時顯示空狀態，兩圖表都不渲染假 slice、NaN 或 0 除法。
- 桌面（>=768px）採 Category、Tag 兩欄；手機垂直堆疊。以現有 CSS/HTML/SVG 或 conic-gradient 完成，不新增 chart library。

## 頁面與互動

`ReportController.Index` 回傳受保護的 View。`report.js` 初次選本月並平行讀取兩端點；Today/Week/Month/Custom 改變後，以相同 query 同步更新兩圖。Custom range 無效時在本地顯示錯誤、不發 request。每次新 request 以 request sequence 忽略舊 response，避免快速切換造成圖表回退。

Category legend 顯示色點、名稱、時長與百分比；Tag row 顯示名稱、時長與相對最大值寬度。字串一律用 `textContent` 產生，避免名稱注入圖表 markup。

## 測試與驗收

ReportService tests 覆蓋 month 預設、preset/custom validation、帳號時區、跨使用者隔離、Uncategorized、Tag additive、排序與空資料。前端驗收確認兩端點同步、空狀態、錯誤、桌面雙欄、320px 垂直布局、Category 百分比與 Tag 無百分比規則。
