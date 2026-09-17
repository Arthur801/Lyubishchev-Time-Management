# Calendar View（第 8 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-calendar-view-design.md`](superpowers/specs/2026-09-17-calendar-view-design.md) 與 [`Docs/superpowers/plans/2026-09-17-calendar-view.md`](superpowers/plans/2026-09-17-calendar-view.md) 實作，這裡記錄實作時的具體事實與跟計畫不同之處。

## 設計思路

Calendar 是 `/TimeEntry` 既有 History 頁面的第二種檢視，不是新頁面、新 Controller 或新資料模型。List／Calendar 用一組 tab 按鈕切換，兩者共用同一份 `GET /api/time-entries` overlap 查詢與既有的 ownership 規則；Calendar 端完全唯讀（V1 無建立/編輯/拖放），不會發出 POST/PATCH/DELETE。

## 整體流程

1. `wwwroot/js/calendar.js` 啟動時呼叫 `GET /api/settings/timezone` 取得帳號時區（失敗時退回瀏覽器時區，與 `time-entry.js` 同樣的容錯模式）。
2. 依 `window.matchMedia('(min-width: 768px)')` 決定 `mode`：`week`（桌面，星期日到星期六）或 `day`（手機，只顯示 anchor date 當天）；resize 跨越斷點時重新抓資料並重繪。
3. 用 `wwwroot/js/timezone.mjs` 新增的 `zonedDayUtcBounds`／`addZonedDays`／`weekdayOfDate`／`getZonedDateParts` 把可視範圍的第一天與最後一天換算成 UTC 半開區間，呼叫 `GET /api/time-entries?startUtc=...&endUtc=...&page=1&pageSize=200`（不做分頁、不跨頁彙總；200 已是 `TimeEntryService.ListAsync` 既有的 pageSize 上限，跟 List View 共用同一段 clamp 邏輯，未新增任何後端程式碼）。
4. `wwwroot/js/calendar-state.mjs`（純函式、無 DOM／無 Intl 依賴，供 Node 測試）把每筆 TimeEntry 依可視的每一個本地日切成顯示用的 segment（`splitEntryIntoDaySegments`，只影響畫面呈現，不改寫原始 UTC interval 或 duration），並用一個貪婪區間著色演算法把同一天內重疊的 segment 分配到不同欄（`layoutOverlappingSegments`，保證每一筆都可見，不隱藏也不合併）。
5. `calendar.js` 依 segment 的 `startMinutes`/`endMinutes`（相對每日 0–1440 分鐘）用 `top`/`height` 百分比把 block 定位在每日的 24 小時時間軸上，`left`/`width` 依 `column`/`columnCount` 分欄。

## 檔案清單

| 檔案 | 內容 |
|---|---|
| `Views/TimeEntry/Index.cshtml` | 新增「列表／行事曆」tab（`data-history-view`）、把既有 List 內容包進 `#list-view`、新增 `#calendar-section`（導覽列 + `#calendar-grid`），載入 `calendar.css`／`calendar.js` |
| `wwwroot/js/time-entry.js` | 新增 tab 切換邏輯（純粹切換 `#list-view`/`#calendar-section` 的 `hidden`，不碰 Calendar 的內部狀態） |
| `wwwroot/js/calendar.js` | Calendar 唯一的狀態與 DOM render 邏輯：week/day 導覽、fetch、呼叫 `calendar-state.mjs` 切段與分欄、render block |
| `wwwroot/js/calendar-state.mjs` | 純函式：`splitEntryIntoDaySegments`、`layoutOverlappingSegments`，供 `calendar.js` 使用、供 Node 測試直接匯入 |
| `wwwroot/js/timezone.mjs` | 新增 `getZonedDateParts`（`getZonedParts` 的公開版本）、`addZonedDays`、`weekdayOfDate`、`zonedDayUtcBounds`；既有匯出（`zonedTimeToUtcIso` 等，供 History List 用）不變 |
| `wwwroot/css/calendar.css` | 桌面 7 欄週格線、手機單欄、24 小時 track 背景線、block 樣式；沿用 `dashboard.css` 既有的 CSS 變數（`--ink`/`--muted`/`--line`/`--surface`/`--accent`） |
| `Tests/Unit/calendar-state.test.mjs` | `splitEntryIntoDaySegments`（同日、跨本地午夜切兩段、範圍外被排除）與 `layoutOverlappingSegments`（不重疊維持同一欄、重疊分不同欄且都不被隱藏、欄位可回收再利用）共 5 個測試 |
| `Tests/Unit/timezone.test.mjs` | 新增 4 個測試涵蓋新匯出的函式 |

## 跟計畫不同之處

- **計畫 Task 1（保護可視範圍資料讀取）沒有新增程式碼或測試**：檢查後發現 `TimeEntryService.ListAsync` 與 `TimeEntryApiController.List` 早就滿足計畫列出的所有條件（`EndTimeUtc > startUtc && StartTimeUtc < endUtc` overlap、`UserId` 擁有權過濾、`AsNoTracking`、`pageSize` clamp 在 `[1,200]`），且 `TimeEntryServiceTests.ListAsync_includes_an_entry_overlapping_the_range_boundary_and_excludes_one_outside_it` 已經覆蓋邊界情境。這是延續 Dashboard/History 既有查詢，不是重新實作，因此沒有重複新增一份幾乎一樣的測試。
- **Calendar 不接 List 的 Category／搜尋篩選**：規格本身沒有要求兩者篩選器互通，Calendar 切換後會隱藏 List 的 `filter-bar`／分頁，只用自己的日期導覽（上一段／今天／下一段），顯示可視範圍內「該使用者的全部」TimeEntry。
- **`calendar.js` 的 timezone 取得方式與 `time-entry.js` 各自獨立呼叫一次 `/api/settings/timezone`**：兩個模組不共用狀態（保持計畫原本「calendar.js 只處理可視範圍、時區轉換和畫面切段」的檔案邊界），多一次輕量 GET 換取模組解耦。

## 手動驗證

本機啟動 `dotnet run --no-build`（port 5180），用 curl 走完整流程：註冊 → `GET /TimeEntry`（200，確認 `#calendar-section`/`#calendar-grid`/`data-history-view`/`calendar.css`/`calendar.js` 皆存在於回應 HTML）→ 確認 `wwwroot/css/calendar.css`、`wwwroot/js/calendar.js`、`wwwroot/js/calendar-state.mjs` 皆可透過靜態檔案中介軟體正常回應 200 → 建立三筆 TimeEntry（同日不重疊、同日重疊、跨本地午夜）→ 用 calendar.js 實際會送出的本週 UTC 範圍（Asia/Taipei，2026-09-12T16:00:00Z–2026-09-19T16:00:00Z）打 `GET /api/time-entries`，確認三筆都正確回傳且都帶正確的 `Z` 尾碼。驗證用的測試資料與帳號已於驗證後刪除。

這個 session 的環境沒有可用的瀏覽器自動化工具（`claude-in-chrome` 技能呼叫時回報未知技能，跟 Dashboard 那次 session 遇到的狀況一樣），因此週欄／單日版面的實際視覺效果、focus 順序與 320px 版型**沒有**在真的瀏覽器裡驗證過。切段（`splitEntryIntoDaySegments`）與分欄（`layoutOverlappingSegments`）演算法改用上述真實資料的數值直接寫進 `Tests/Unit/calendar-state.test.mjs`，把邏輯正確性驗證在單元測試層級；接手後建議優先補瀏覽器視覺驗證這一步。

## 尚未涵蓋的部分

- 完全比照設計文件「不在範圍」：Calendar 無建立/編輯/拖放/resize；不影響 Dashboard／Report／Category／Tag 管理；不修改 TimeEntry CRUD 語意。
- Calendar 目前是獨立於 List 篩選器之外的唯讀檢視，尚未支援「只看某分類」這類篩選（設計文件本來就沒有要求，如果之後要加，建議跟 List 的 `#filter-category` 共用同一個 state 來源，而不是在 Calendar 內再做一份篩選 UI）。
