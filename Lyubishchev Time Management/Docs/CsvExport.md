# CSV Export（第 13 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-csv-export-design.md`](superpowers/specs/2026-09-17-csv-export-design.md) 與 [`Docs/superpowers/plans/2026-09-17-csv-export.md`](superpowers/plans/2026-09-17-csv-export.md) 實作，這裡記錄實作時的具體事實與跟計畫不同之處。

## 設計思路

CSV export 是 History 的明細下載，不是彙總報表，也不是備份格式。只掛在 `/TimeEntry`，用跟清單完全一樣的有效篩選值（`startUtc`/`endUtc`/`categoryId`/`search`）打 `GET /api/time-entries/export`，差別只在於不分頁、輸出格式是 CSV。刻意不重用 `TimeAggregationService`——那個服務會把 TimeEntry 依查詢邊界裁切成統計用的區間交集，匯出需要的是每一筆的原始 `StartTimeUtc`/`EndTimeUtc`/時長，裁切會讓明細失真。

## 整體流程

1. `CsvExportService.ExportAsync` 讀取 `UserSettingsService.GetAsync` 取得帳號目前的 `TimeZoneId`，用 `TimeZoneCatalog.ResolveOrUtc` 解析成 `TimeZoneInfo`。
2. 查詢邏輯跟 `TimeEntryService.ListAsync` 完全同構（`UserId` 擁有權、`EndTimeUtc > startUtc`/`StartTimeUtc < endUtc` 半開區間 overlap、`categoryId` 篩選、名稱/Tag 的 `EF.Functions.Like` 搜尋、`Include(Category)`/`Include(TimeEntryTags.Tag)`、依 `StartTimeUtc` 降冪再依 `(long)Id` 降冪排序），差別是**完全不做 `Skip`/`Take`**——這是刻意不抽共用方法的地方，維持匯出「無分頁上限」與清單「50 筆/頁」是兩份獨立、各自完整的查詢。
3. 每筆 TimeEntry 轉成 8 欄字串（`Date`/`Start Time`/`End Time`/`Duration`/`Name`/`Category`/`Tags`/`Time Zone`），本地時間用 `TimeZoneInfo.ConvertTimeFromUtc` 分別轉換 Start/End（不是用本地顯示值相減），`Duration` 一律用 `EndTimeUtc - StartTimeUtc`（UTC 差值），DST 換日也不會讓時長算錯。
4. `CsvWriter.Write(headers, rows)` 產生 RFC 4180 的 UTF-8 BOM 位元組陣列，`Category`/`Name` 以外沒有特別處理，但所有欄位都經過同一個 escape/formula-neutralize 流程。
5. `TimeEntryApiController.Export` 是純轉接：驗證 `endUtc > startUtc`（跟 `List` 的 `INVALID_TIME_RANGE` 同一段邏輯）、取得 `CurrentUserService.GetRequiredUserId()`、呼叫 service、回傳 `File(bytes, "text/csv; charset=utf-8", fileName)`。不掛 `[ValidateAntiForgeryToken]`（安全的 GET，無狀態變更），不接受 `userId`/時區/`page`/`pageSize`。
6. 前端 `time-entry.js` 的「匯出 CSV」按鈕不呼叫 `callApi()`（那個函式會 `response.json()`，會把瀏覽器原生下載流程弄壞），改用 `window.location.assign()` 做一般導覽下載，query string 完全複用清單目前生效的篩選（`buildListQuery(false)`，只是省略 `page`/`pageSize`）。

## 跟計畫不同之處

- **修正計畫範例程式碼裡一個真的 bug**：`Infrastructure/Csv/CsvWriter.cs` 原始草稿用 `Utf8WithBom.GetBytes(text)` 直接產生位元組，但 `Encoding.GetBytes()` 不論建構子的 `encoderShouldEmitUTF8Identifier` 設定為何，**都不會**在輸出前面加上 BOM——那個旗標只影響 `GetPreamble()`（通常給 `StreamWriter` 在寫入串流開頭時使用）。寫完 `CsvWriterTests` 照計畫的斷言跑起來直接全部失敗在 BOM 檢查（回傳的前三個 byte 是 `D`/`a`/`t` 而不是 `EF BB BF`）。修正方式：`Write` 內明確 `preamble = Utf8WithBom.GetPreamble()`、`body = Utf8WithBom.GetBytes(text)`，手動把兩段位元組併起來回傳。**教訓：計畫文件裡的範例程式碼不是自動正確的，還是要照著寫失敗測試、真的跑一次再往下走**——這次剛好靠 Task 1 自己要求的「先寫失敗測試」流程接住了。
- **`Category` 欄位的「未分類」值故意跟 UI 不一樣**：設計文件明講欄位值是英文字面量 `Uncategorized`，跟 App 介面（Dashboard/`TimeAggregationService`）用的中文「未分類」不同調——這是 CSV 匯出格式自己的既定規格，不是疏漏，程式碼裡有加註解說明避免以後被「統一成中文」誤改掉。
- **沒有額外的 `Infrastructure/Logging` CSV export failure 事件**：設計文件與 AGENTS.md 都要求匯出失敗要記錄，但目前專案的 `Infrastructure/Logging` 只有 auth 事件這一種 logger，全域例外處理本身也還沒做（見 `Docs/TODO.md` 第 15 項）。比照專案既有慣例，這類跨功能的 logging 基礎設施留給第 15 項一次補齊，不在這次單獨補一個只給 CSV 用的 logger。

## 檔案清單

| 檔案 | 內容 |
|---|---|
| `Infrastructure/Csv/CsvWriter.cs` | 無狀態 RFC 4180 編碼器：UTF-8 BOM（手動附加 preamble）、CRLF、逗號/雙引號/CR/LF 需要時加引號、公式字元（`=`/`+`/`-`/`@`）開頭的欄位加單引號防止試算表當成公式執行 |
| `Services/CsvExportService.cs` | `CsvExportRequest`（`StartUtc`/`EndUtc`/`CategoryId`/`Search`）、`CsvExportDocument`（`Content`/`FileName`）、`ExportAsync`：不分頁查詢 + 帳號時區轉換 + row 組裝 + 檔名決定 |
| `Controllers/Api/TimeEntryApiController.cs` | 新增 `GET /api/time-entries/export`，`[Authorize]`，不掛 CSRF（安全 GET） |
| `Program.cs` | `builder.Services.AddScoped<CsvExportService>();` |
| `Views/TimeEntry/Index.cshtml` | `page-heading` 新增 `.page-heading__actions` 容器，內含「匯出 CSV」`#export-csv` 按鈕（在「+ 新增紀錄」之前） |
| `wwwroot/js/time-entry.js` | `buildListQuery(includePagination = true)` 支援省略 `page`/`pageSize`；`#export-csv` click handler 用 `window.location.assign` 導覽下載，1 秒後解除按鈕的暫時 disabled 狀態 |
| `wwwroot/css/history.css` | `.page-heading__actions`（flex + gap，讓匯出／新增兩個按鈕並排，窄螢幕隨 `dashboard.css` 既有的 `.page-heading{display:grid}` media query 自然換行） |
| `Tests/TimeEntryFlow.Tests/Unit/CsvWriterTests.cs` | BOM/CRLF/escape/公式中和、無資料列時只有 header |
| `Tests/TimeEntryFlow.Tests/Integration/CsvExportServiceTests.cs` | 完整 row（含排序、時區、Uncategorized、Tag 排序）、跨午夜範圍內完整一筆、201 筆驗證無分頁上限、跨使用者隔離＋空結果只剩 header、New York 春季 DST 位移（時長仍是 UTC 差值而非牆上鐘面相減） |

沒有動到任何 migration、Entity 或第三方 CSV 套件，符合計畫預期。

## 手動驗證

本機啟動 `dotnet run --no-build`（port 5180），用 `curl` 走完整流程：註冊 → 建立 Category「Work」→ 建立一筆有分類/雙 Tag 的 TimeEntry（`2026-09-17 14:00–15:30` Taipei）與一筆跨本地午夜的未分類 TimeEntry（`2026-09-17 23:00–2026-09-18 01:00` Taipei）→

- `GET /api/time-entries/export`（不帶範圍）：`200`，`Content-Type: text/csv; charset=utf-8`，`Content-Disposition: attachment; filename=time-entries-all.csv`，位元組開頭是 `EF BB BF`，兩筆資料依 `StartTimeUtc` 降冪排列，欄位（含 `Alpha; zeta` 排序、`Uncategorized`、`01:30:00`/`02:00:00` 時長）皆正確。
- 帶今天（Asia/Taipei）的 `startUtc`/`endUtc`：檔名變成 `time-entries-20260917-20260917.csv`。
- 帶 `categoryId`：只回傳該分類的那一筆。
- `endUtc <= startUtc`：`400 INVALID_TIME_RANGE`。
- 未登入直接打端點：`401`。
- `GET /TimeEntry` 頁面含 `id="export-csv"` 按鈕（文字「匯出 CSV」）。

驗證用的測試資料（TimeEntry、Category、帳號）已於驗證後刪除。

## 尚未涵蓋的部分

完全比照設計文件「不在範圍」——沒有新的匯出頁面、儲存的篩選預設、排程匯出、背景工作或報表匯出；沒有動到 `TimeEntryService.ListAsync` 本身（只是仿照它的查詢邏輯，各自獨立）。CSV export failure 的 log 事件如上所述留給第 15 項一起補。
