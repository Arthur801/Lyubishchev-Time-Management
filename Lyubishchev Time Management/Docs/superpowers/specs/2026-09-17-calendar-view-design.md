# Calendar View 設計

**日期：** 2026-09-17

**範圍：** 整合在 `/TimeEntry` 的 Calendar View、可視範圍讀取、桌面週時間軸與行動裝置單日檢視。
**不在範圍：** 建立、編輯、拖放、resize、排程、Dashboard／Report、Category／Tag 管理與修改 TimeEntry 的既有 CRUD 語意。

## 目標

將 History 的 List 與 Calendar 作為同一個 TimeEntry 功能的兩種檢視。Calendar 只顯示目前可視日期範圍內、屬於目前使用者的 TimeEntry，以 Category 色彩、未分類 fallback 與 Tag chip 呈現，並在桌面與手機提供可讀的不同版型。

## 導覽與狀態

`/TimeEntry` 頁面新增「列表／行事曆」切換；List 維持現有預設。Calendar state 只有 `view`、`anchorDate` 與可視範圍，前端不把所有紀錄快取成行事曆資料庫。上一段、下一段與今天按鈕改變 anchor date 後，重新查詢 API。

桌面寬度（>=768px）固定顯示星期日到星期六的週時間軸；手機（<768px）固定顯示 anchor date 的單日時間軸。行動版的上一段／下一段移動一天，桌面版移動一週。週起點與既有 History 一致，都是星期日。

## 資料讀取與時區

Calendar 不新增平行的 Entity 或重複 response。後端重用 `TimeEntryResponse` 與既有 ownership 規則，以 UTC 半開範圍取得候選資料：

```text
entry.StartTimeUtc < visibleEndUtc
AND entry.EndTimeUtc > visibleStartUtc
```

Calendar 可使用擴充後的 `GET /api/time-entries` range query；它不能用無範圍或分頁模式載入所有使用者紀錄。當第 12 項 Timezone Settings 完成後，前端以帳號 `TimeZoneId` 將星期日／本地日界線轉成 UTC。該設定尚未落地前，Calendar 不能把瀏覽器時區當成永久資料規則；實作順序應先完成時區設定，或在 Calendar 實作中明確注入相同的設定 response。

## 顯示規則

- 原始 TimeEntry 保持單一 UTC interval；若它跨越本地日界線，`calendar.js` 僅為顯示目的依每日可視區間切成多個 block。
- Category 色彩使用 `categoryColor`；無 Category 使用 `#a6adb7`，名稱顯示「未分類」。
- Tag 使用既有 tag chip 視覺；無 Tag 不顯示空容器。
- 重疊紀錄可並列或堆疊，但不得隱藏任一筆，也不得合併或改寫其 duration。
- 顯示時間一律轉換成帳號時區；跨日 block 在每一日列內裁切，tooltip／可存取名稱仍應包含原始活動名稱、當地開始／結束時間與時長。
- Calendar V1 唯讀。點擊項目不開啟編輯器；使用者改到 List view 後仍使用既有編輯／刪除功能。

## 結構與檔案邊界

| 檔案 | 責任 |
| --- | --- |
| `Controllers/TimeEntryController.cs` | 提供同一 History 頁面與必要初始 ViewModel。 |
| `Controllers/Api/TimeEntryApiController.cs` | 驗證 visible UTC range 並重用 TimeEntry service 查詢。 |
| `Services/TimeEntryService.cs` | 只做擁有權與 overlap 範圍查詢；不放 CSS 切段邏輯。 |
| `Models/ViewModels/HistoryViewModel.cs` | 提供 Category 與未來時區設定的初始資料。 |
| `Views/TimeEntry/Index.cshtml` | List／Calendar 區塊、切換按鈕、可存取標記。 |
| `wwwroot/js/calendar.js` | 日期導航、時區轉換、block 切段與 DOM render。 |
| `wwwroot/css/calendar.css` | 桌面週欄、行動單日、重疊 block 與 RWD。 |

## 測試與驗收

後端測試延用 SQLite `TimeEntryFlow.Tests`：可視範圍 overlap、跨使用者隔離、跨午夜候選資料與無效 range。前端以手動驗收確認桌面週欄、320px 單日檢視、前後導航、Today、未分類、Tag、跨日及重疊項目皆可讀。

完成標準：不載入全歷史資料；不修改 UTC 時間或 TimeEntry CRUD；桌面是週時間軸、手機是單日導向；所有範圍與本地顯示都遵守帳號時區設定。
