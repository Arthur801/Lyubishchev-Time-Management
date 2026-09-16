# Category 與 Tag 管理設計

**日期：** 2026-09-17

**範圍：** Category 與 Tag 的管理頁、服務層、CRUD API、資料庫約束與整合測試。
**不在範圍：** TimeEntry CRUD、inline Tag 建立流程的重新設計、Dashboard/Report 彙總、時區設定與 CSV 匯出。

## 目標

完成個人 Category 與 Tag 的獨立管理功能。使用者可以建立、列出、修改與刪除自己擁有的項目；所有資料存取都以 JWT 的目前使用者為範圍。Category 與 Tag 都不得在同一使用者底下有重複名稱。

## 選定的設計方向

採用兩個獨立管理頁與兩組對稱的 JSON API：`/Category` 搭配 `/api/categories`，`/Tag` 搭配 `/api/tags`。各自的 Razor View 只提供頁面骨架，`category.js`、`tag.js` 負責呼叫 API 與更新本頁 UI；Controller 保持薄層，所有擁有權、名稱衝突、時間戳與刪除行為都放在對應 Service。

不把管理功能塞入 TimeEntry 編輯視窗。TimeEntry 仍透過現有的分類下拉選單使用 Category，並維持現有的 inline find-or-create Tag 行為。兩條路徑共用同一份名稱正規化規則，避免管理頁建立的 `Focus` 與 inline 建立的 ` focus ` 產生不一致的結果。

## 名稱與資料庫規則

- Category 與 Tag 名稱都先去除前後空白，空白結果無效；最大長度維持 100 個字元。
- 相同性以去除前後空白後、忽略大小寫比較。例如 `Focus`、`focus` 與 ` focus ` 是同一個名稱。
- 儲存原始但已 Trim 的大小寫，首次建立者的大小寫成為顯示值；改名成功時才改變顯示大小寫。
- `CategoryConfiguration` 的 `(UserId, Name)` 索引改為唯一索引，與既有 Tag 唯一索引一致；新增 EF Core migration。
- MySQL 資料庫的唯一索引是最後防線。Service 先查詢提供可讀錯誤；遇到併發寫入造成的 `DbUpdateException` 時，轉換為同一個衝突結果，而非 500。
- Tag 的 inline find-or-create 也使用這個正規化與比較規則；它遇到唯一索引競爭時，延續目前的 detach、重新讀取及重試結果策略。

## API 契約

兩種資源各自使用 `CategoryRequest`／`TagRequest` 作為新增及完整更新的 body。Category request 含 `name`、`color`，Tag request 僅含 `name`。兩者都使用 data annotations 驗證必填與最大長度；Category 的色彩必須是 `#RRGGBB` 格式。

| 操作 | Category | Tag | 成功回應 |
| --- | --- | --- | --- |
| 列表 | `GET /api/categories` | `GET /api/tags` | 200，依名稱不分大小寫排序的 response 陣列 |
| 建立 | `POST /api/categories` | `POST /api/tags` | 201，建立後 response |
| 更新 | `PATCH /api/categories/{id}` | `PATCH /api/tags/{id}` | 200，更新後 response |
| 刪除 | `DELETE /api/categories/{id}` | `DELETE /api/tags/{id}` | 204 |

- 每個 API action 都有 `[Authorize]`；POST、PATCH、DELETE 都有 `[ValidateAntiForgeryToken]`。
- response 為 `CategoryResponse(id, name, color)` 或 `TagResponse(id, name)`；不回傳 `UserId` 或內部導航屬性。
- `ModelState` 無效回傳 `ValidationProblem(ModelState)`（400）。
- 同名建立或改名回傳 409，title 分別是 `CATEGORY_NAME_CONFLICT`、`TAG_NAME_CONFLICT`。
- 非本人或不存在的 id 對更新與刪除皆回傳 404，title 分別是 `CATEGORY_NOT_FOUND`、`TAG_NOT_FOUND`。
- 列表只查詢目前使用者資料，因此不接受或信任前端 UserId。

## Service 與刪除語意

`CategoryService`、`TagService` 各自提供 `ListAsync`、`CreateAsync`、`UpdateAsync`、`DeleteAsync`，並以各 feature 專屬 result record 將成功 payload 或 `ErrorCode`／`ErrorMessage` 回傳 Controller。兩個 Service 都注入 `AppDbContext` 與 `IClock`，只在建立或更新時寫入對應 UTC 時間戳。

刪除不額外刪除 TimeEntry：

- Category 刪除採既有 `TimeEntries.CategoryId` 的 `ON DELETE SET NULL`。紀錄保留，之後呈現為未分類。
- Tag 刪除採既有 `TimeEntryTags.TagId` 的 `ON DELETE CASCADE`。關聯列移除，TimeEntry 保留。

此行為由資料庫外鍵保證；Service 不自行載入並逐筆修改 TimeEntry 或關聯列。

## 管理頁與前端互動

- `CategoryController.Index` 與 `TagController.Index` 僅回傳對應 View；兩者必須 `[Authorize]`。
- 既有側欄的「分類」、「標籤」由 `#` placeholder 改成這兩條實際路由，並於各管理頁正確標記 active 狀態；Dashboard 和 History 的共用導覽也一併對齊。
- Category 頁以列表顯示色票、名稱與每列的編輯／刪除操作；新增與編輯共用 modal 或 inline form，色彩輸入採原生 color input 加可見的 hex 值。
- Tag 頁以 tag chip／列表顯示名稱與編輯／刪除操作；新增與編輯的互動與 Category 頁一致，但不顯示色彩欄位。
- 刪除前使用原生確認對話框，文案明確說明 Category 會使既有紀錄改為未分類，Tag 只會移除紀錄上的標籤。
- JavaScript 的 `callApi()` 延續 `timer.js` 與 `time-entry.js` 慣例：非 GET 請求附上 `X-CSRF-TOKEN`，安全處理 JSON／空回應，並以 Problem Details 的 `detail` 呈現可讀錯誤。
- 成功新增、更新、刪除後重新取得本資源列表；不在客戶端推測唯一性或關聯結果。

## 測試

測試延用 `Tests/TimeEntryFlow.Tests` 的 SQLite shared in-memory helper，因為它可驗證唯一索引、外鍵 `SET NULL`／`CASCADE`，不像 EF InMemory provider。

新增 `CategoryServiceTests` 與 `TagServiceTests`，覆蓋：

1. 自己的項目可完整 CRUD，列表依不分大小寫名稱排序，時間戳使用 `TestClock`。
2. Trim 與不分大小寫的同名新增／改名會得到對應 409 conflict result。
3. 兩個獨立 `DbContext` 併發建立相同名稱後，最終只存在一筆資源，失敗請求回傳 conflict 而非未處理例外。
4. 跨使用者的更新與刪除回傳 not found，且不改變原始資料。
5. 刪除 Category 後 TimeEntry 保留且 `CategoryId` 為 null；刪除 Tag 後 TimeEntry 保留且 join rows 被清除。
6. TimeEntry inline Tag 建立使用與 Tag 管理相同的正規化規則，並且不因不同大小寫建立第二筆 Tag。

## 驗收條件

1. 已登入使用者能從側欄進入 Category 與 Tag 管理頁，完成新增、更新、刪除並在刷新後看見正確資料。
2. 同一使用者無法建立或改名為同一正規化名稱；不同使用者可各自有同名項目。
3. CSRF、JWT 授權、擁有權與 Problem Details 錯誤格式和既有 API 一致。
4. Category／Tag 刪除符合關聯資料保留規則，且由 SQLite 整合測試驗證。
5. 既有 TimeEntry 的分類下拉與 inline Tag 建立仍可運作，且 inline Tag 不會繞過名稱唯一規則。

## 設計檢查

- 沒有 TBD、TODO 或未決名稱、錯誤碼、資料庫與 UI 行為。
- API、Service、資料庫約束與測試對「Trim + 忽略大小寫的一位使用者一名稱」採一致定義。
- 範圍限於 Category／Tag 管理；沒有擴張到 Dashboard、Report、時區或任務管理功能。
