# Timezone Settings 與 Time Aggregation 設計

**日期：** 2026-09-17

**範圍：** 第 12 項 Timezone settings 與第 9 項 TimeAggregationService 的共用時間規則、服務 API、設定頁與單元測試。
**不在範圍：** Dashboard／Report 真實資料 API、Calendar View、CSV export 與既有 TimeEntry CRUD 表單的前端改造；這些功能將在後續工作消費本設計定義的服務。

## 目標

讓每位使用者在受控的常用 IANA 時區清單中選擇自己的時區，而不改寫任何已存的 UTC 時間。建立可重用、可測試的 TimeAggregationService，將使用者本地日期區間正確轉成 UTC、以交集計算跨界 TimeEntry 的時長，並輸出日別、Category 與 Tag 統計。

## 選定方向與依賴順序

先完成 Timezone settings，再實作 TimeAggregationService。`UserSettingsService` 是唯一能寫入 `User.TimeZoneId` 的應用服務；它使用固定白名單驗證輸入。`TimeAggregationService` 只讀取一個已驗證的 IANA ID，絕不由瀏覽器時區或伺服器本地時區推測範圍。

兩個服務共享 `TimeZoneCatalog`：它集中儲存可選項目、解析 Windows/.NET 的 `TimeZoneInfo`、將無效設定降級為 `UTC` 的讀取防線。正常的設定更新不會產生無效值；此 fallback 只保護舊資料、手動資料修復或未來部署錯誤，不將例外洩漏到統計 API。

## 時區設定

### 可選時區

使用固定、可讀的常用 IANA 時區清單；每項含 ID 與繁體中文顯示名稱：

| IANA ID | 顯示名稱 |
| --- | --- |
| `UTC` | 協調世界時間 (UTC) |
| `Asia/Taipei` | 台北 (UTC+08:00) |
| `Asia/Tokyo` | 東京 (UTC+09:00) |
| `Asia/Singapore` | 新加坡 (UTC+08:00) |
| `Asia/Hong_Kong` | 香港 (UTC+08:00) |
| `Europe/London` | 倫敦 |
| `Europe/Paris` | 巴黎 |
| `America/New_York` | 紐約 |
| `America/Chicago` | 芝加哥 |
| `America/Denver` | 丹佛 |
| `America/Los_Angeles` | 洛杉磯 |
| `Australia/Sydney` | 雪梨 |
| `Pacific/Auckland` | 奧克蘭 |

清單是後端常數，Razor 與 JavaScript 不自行複製。`GET /api/settings/timezone` 回傳目前設定及完整選項；`PATCH /api/settings/timezone` 只接受 `{ "timeZoneId": "Asia/Taipei" }`。

### 更新規則與錯誤

- API 由 `[Authorize]` 保護；PATCH 必須帶 antiforgery token。
- Controller 從 `CurrentUserService` 取得 UserId，不接受前端 UserId。
- 缺少、空白或不在 `TimeZoneCatalog` 的 ID 回傳 400 `INVALID_TIME_ZONE`；找不到已登入的 User 時回傳 404 `USER_NOT_FOUND`。
- 合法更新只變更 `User.TimeZoneId` 與 `UpdatedAtUtc`，時間取自 `IClock.UtcNow`；`CreatedAtUtc`、RunningTimer、TimeEntry 的 UTC 欄位都不可修改。
- 成功更新後回傳新的設定 response。重複選擇目前時區仍回 200，並更新 `UpdatedAtUtc`，使 PATCH 的結果可預期。

### 設定頁

`/Settings` 是受保護頁面，提供目前時區的 select、儲存按鈕與成功／失敗訊息。選項依後端 API 載入；不使用瀏覽器的 `Intl.DateTimeFormat().resolvedOptions().timeZone` 自動覆寫帳號值。頁面可顯示瀏覽器偵測時區作為文字提示，但不會自動提交。

## 共同時間模型

### 輸入與邊界

Aggregation 接收 `LocalDateRange(DateOnly StartDate, DateOnly EndDateInclusive)` 與使用者 IANA ID；它拒絕 `EndDateInclusive < StartDate`。服務將日期轉為當地日界線，再產生 UTC 半開區間 `[rangeStartUtc, rangeEndUtc)`：

```text
local start: StartDate 00:00
local end:   EndDateInclusive + 1 day 00:00
utc range:  [ConvertToUtc(local start), ConvertToUtc(local end))
```

週範圍從星期日開始，結束於下個星期日開始之前；月份範圍從該月 1 日到下月 1 日。Today、ThisWeek、ThisMonth 與 Custom range 的 helper 都使用使用者時區的目前日期，而不是伺服器日期。

對日光節約時間轉換使用 `TimeZoneInfo.ConvertTimeToUtc`：當本地午夜有無效時間時，向前尋找第一個有效的本地時間；當午夜有歧義時，選擇較早的 UTC 瞬間。此規則讓範圍連續且不重複計算。

### 交集與統計

所有計算都採半開 UTC interval。單筆 TimeEntry 的計入時段為：

```text
overlapStart = max(entry.StartTimeUtc, rangeStartUtc)
overlapEnd   = min(entry.EndTimeUtc, rangeEndUtc)
duration     = max(0, overlapEnd - overlapStart)
```

查詢先過濾 `entry.UserId == userId`，再以 `entry.StartTimeUtc < rangeEndUtc && entry.EndTimeUtc > rangeStartUtc` 取得候選資料。服務載入 Category 與 Tags，僅在記憶體內進行 `TimeZoneInfo` 與 `DateOnly` 分桶；不把不可翻譯的時區函式放進 EF query。

- 總時長是所有 TimeEntry 的 interval duration 直接相加；重疊紀錄可使一天超過 24 小時。
- Daily aggregation 會依使用者當地日期切開跨日 TimeEntry，每天只加該日交集；在輸入日期範圍內，即使沒有紀錄也輸出 duration 為 0 的日別項。
- Category aggregation 以每筆 interval 的全時長歸入其 Category；沒有 Category 的紀錄歸入 `Uncategorized`，顏色固定 `#a6adb7`。Category 總和等於總時長。
- Tag aggregation 以每筆 interval 的全時長加到每一個 Tag；未標記的紀錄不產生 Tag bucket。Tag 總和可大於總時長，這是預期行為。

## Service 介面與 DTO

`TimeAggregationService` 依賴 `AppDbContext`、`IClock`、`TimeZoneCatalog`。它提供：

- `GetRangeForPreset(DateRangePreset preset, string timeZoneId)`：回傳 local date range，支援 `Today`、`ThisWeek`、`ThisMonth`。
- `AggregateAsync(ulong userId, LocalDateRange range, string timeZoneId, CancellationToken)`：回傳 `TimeAggregationResult`。

結果包含：

- `RangeStartUtc`、`RangeEndUtc`、`TimeZoneId`、`TotalSeconds`。
- `DailyTotals`：每個本地日期、秒數。
- `CategoryTotals`：nullable `CategoryId`、名稱、色彩、秒數，含 Uncategorized。
- `TagTotals`：TagId、名稱、秒數。

DTO 不回傳 Entity navigation、UserId 或未篩選的 TimeEntry；Dashboard／Report 將把這些純統計值轉換成自己的 response。

## 測試策略

`TimeZoneCatalog` 與 `TimeAggregationService` 放入獨立 unit test 專案或現有 `TimeEntryFlow.Tests`，取決於是否需要 SQLite 的真實關聯資料；聚合測試需要 TimeEntry、Category、Tag 與關聯，因此延用 `TimeEntryFlow.Tests` 的 SQLite shared in-memory setup。

必要案例：

1. Catalog 只接受列出的 IANA ID，並解析 `Asia/Taipei`、`America/New_York`。
2. 時區更新修改 `TimeZoneId` 與 `UpdatedAtUtc`，不改任何 TimeEntry UTC 值；跨使用者更新不可行。
3. 台北時區的本日、本週（星期日開始）、本月與 custom range 轉成正確 UTC 範圍。
4. 一筆 23:00→01:00 的紀錄在兩個本地日各得到正確的時長；範圍邊界外資料不計入。
5. `America/New_York` 的春季 DST 缺失小時與秋季重複小時，範圍仍連續且總時長正確。
6. 未分類 bucket、Category bucket、Tag additive bucket、零紀錄日以及重疊 TimeEntry 皆符合規則。
7. Aggregation query 只讀取目前使用者且只載入範圍相交的 TimeEntries。

## 驗收條件

1. 使用者可在 `/Settings` 選擇白名單時區、重新載入後仍看見自己的選擇，且無效 ID 得到可讀 400 Problem Details。
2. 改時區不改寫任何已存 UTC 時間，但同一批紀錄在新的本地日期檢視中可落入不同日別，這是預期行為。
3. 任何日期範圍都以使用者設定時區計算，週從星期日開始；Dashboard／Report 未來使用相同結果不會各自重寫交集邏輯。
4. 交集、跨午夜、DST、Uncategorized、Tag additive 與跨使用者隔離都有自動化測試。

## 設計檢查

- 沒有 TBD、TODO 或未決的時區清單、週起點、交集定義、錯誤碼與 DST 行為。
- 時區設定只改 User 設定；聚合只讀資料並輸出 DTO，兩者責任不重疊。
- 範圍限定為第 9、12 項的可重用基礎，不提前實作 Dashboard、Report、Calendar 或 CSV。
