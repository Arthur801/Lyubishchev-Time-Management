# Report AI 時間趨勢分析設計規格

**日期：** 2026-09-28  
**狀態：** 已提案，尚未實作  
**範圍：** Report 頁面中的使用者主動式時間趨勢洞察

## 1. 目標與範圍

在 Report 頁面讓已登入使用者針對目前選取的日期區間，主動要求產生繁體中文的時間使用趨勢分析。分析只使用伺服器已計算且經使用者時區校正的彙總資料，協助使用者理解工時分布與變化。

本功能是既有報表的選用洞察，不是通用 AI 助理。它不會寫入、修改或刪除 TimeEntry，也不會自動執行、主動推播或跨使用者比較。

### 1.1 第一版使用者流程

1. 使用者在 `/Report` 選擇 Today、Week、Month 或自訂日期區間。
2. 既有 Category 與 Tag 圖表照常載入。
3. 使用者按下「分析時間趨勢」按鈕。
4. 瀏覽器向 ASP.NET Core 的受保護 API 提出分析請求。
5. ASP.NET Core 依 JWT 的使用者身分取得同一區間的統計資料，呼叫僅限本機存取的 Python 服務。
6. Python 服務呼叫 OpenAI API，將結構化洞察回傳給 ASP.NET Core；前端顯示結果。

### 1.2 非目標

- 不傳送 TimeEntry 名稱、描述或原始逐筆紀錄給 OpenAI。
- 不提供聊天、任務排程、行事曆規劃、代替使用者決策或生產力評分。
- 不以 AI 重新計算工時、日期、百分比、時區或跨日區間。
- 不儲存 OpenAI API 金鑰、完整 prompt 或模型原始回應至資料庫。
- 不使 Python 服務可由網際網路、瀏覽器或 Nginx 公開存取。

## 2. 既有資料與正確性來源

`TimeAggregationService.AggregateAsync` 是唯一的時間彙總來源。它已負責：

- 依使用者 `TimeZoneId` 將本地日期範圍轉為 UTC；
- 以 interval intersection 計算跨查詢邊界及跨日 TimeEntry；
- 產出 `DailyTotals`、`CategoryTotals`、`TagTotals` 與 `TotalSeconds`。

AI 趨勢功能必須消費這個結果，不得在 `ReportService`、Controller 或 Python 重寫時區、每日切分、分類或標籤加總規則。Tag 總計保持既有加總語意：一筆具有多個標籤的紀錄，其完整時間會計入每一個標籤，因此 Tag 總和可能大於 `TotalSeconds`。

## 3. 架構與信任邊界

```text
Browser
  | POST /api/reports/trend-analysis（JWT Cookie + Anti-forgery）
  v
ASP.NET Core Report API
  | 取得 CurrentUserService 的 UserId
  | ReportService + TimeAggregationService 產生最小化彙總資料
  | server-to-server HTTP
  v
Python FastAPI（127.0.0.1 only）
  | OPENAI_API_KEY
  v
OpenAI Responses API
```

### 3.1 ASP.NET Core 職責

- 驗證 `[Authorize]`、anti-forgery 與請求日期區間。
- 永遠由 `CurrentUserService.GetRequiredUserId()` 取得使用者，不接受前端 `UserId`。
- 使用 `ReportService` 解析日期區間，並消費 `TimeAggregationService` 的結果。
- 計算確定性的衍生指標，例如最高/最低日、前後半段比較、平均每日工時與資料天數。
- 將最小化 DTO 傳給 Python，並將其結構化回應映射為前端 API 回應。
- 設定逾時、冷卻時間與錯誤映射；AI 失敗不得影響既有報表 API。

### 3.2 Python 職責

- 提供單一內部 `POST /analyze-time-trend` 端點。
- 驗證 ASP.NET Core 傳入 DTO 的 schema、上限與服務對服務驗證資訊。
- 使用官方 OpenAI Python SDK 呼叫 Responses API。
- 使用 Structured Outputs 強制模型回應符合本規格的 JSON schema。
- 不連線 MySQL、不接受瀏覽器 Cookie、不解析 JWT，也不擁有使用者資料查詢權限。

### 3.3 部署

- Python 以 systemd 執行，僅綁定 `127.0.0.1:<private-port>`；Nginx 不建立 location 或公開 port。
- ASP.NET Core 以 `IHttpClientFactory` 呼叫 Python 的 loopback 位址，並使用短逾時（建議 20 秒）。
- 開發環境的 `OPENAI_API_KEY` 放在 Python 的本機環境檔；正式環境放在 `/etc/ltm/ltm.env` 或 Python 專用的 systemd `EnvironmentFile`。
- API 金鑰不得出現在 Razor、JavaScript、`appsettings*.json`、Git、例外回應或一般日誌中。

## 4. API 合約

### 4.1 瀏覽器至 ASP.NET Core

```text
POST /api/reports/trend-analysis
Content-Type: application/json
X-CSRF-TOKEN: <anti-forgery token>
```

Request：

```json
{
  "preset": "month",
  "startDate": null,
  "endDate": null
}
```

Request 的日期規則與既有 Report 相同：只接受 `today`、`week`、`month`，或同時提供的 `startDate` 與 `endDate`；兩種模式不可混用。

成功回應：

```json
{
  "startDate": "2026-09-01",
  "endDateInclusive": "2026-09-30",
  "timeZoneId": "Asia/Taipei",
  "analysis": {
    "dataSufficiency": "sufficient",
    "summary": "本月共記錄 42 小時 30 分，後半月較前半月增加。",
    "observations": [
      {
        "claim": "週一平均投入時間最高。",
        "evidence": "4 個週一平均為 3 小時 12 分。"
      }
    ],
    "suggestions": [
      "若要安排需要長時間專注的工作，可優先保留週一的可用時段。"
    ],
    "disclaimer": "此分析依據所選期間的工時彙總，不代表工作品質或健康建議。"
  }
}
```

### 4.2 ASP.NET Core 至 Python

Python 只接收下列最小化資料，不接收 email、UserId、TimeEntry 名稱、原始 UTC 時間或 Cookie：

```json
{
  "range": {
    "startDate": "2026-09-01",
    "endDateInclusive": "2026-09-30",
    "timeZoneId": "Asia/Taipei"
  },
  "dailyTotals": [
    { "date": "2026-09-01", "durationSeconds": 7200 }
  ],
  "categoryTotals": [
    { "name": "工作", "durationSeconds": 36000 }
  ],
  "tagTotals": [
    { "name": "專注", "durationSeconds": 25200 }
  ],
  "derivedMetrics": {
    "totalSeconds": 153000,
    "trackedDayCount": 18,
    "calendarDayCount": 30,
    "averageSecondsPerTrackedDay": 8500,
    "firstHalfSeconds": 69000,
    "secondHalfSeconds": 84000
  }
}
```

第一版上限為 365 個日資料點、前 10 個分類與前 10 個標籤；超出時由 ASP.NET Core 以既有排序規則截取並在 prompt 註明「僅提供前 N 名」。未來若需更長區間，應先設計週或月粒度的下採樣規則。

### 4.3 Python 回應 schema

Structured Output 的 schema 固定包含：

| 欄位 | 型別與限制 |
| --- | --- |
| `dataSufficiency` | `sufficient`、`limited` 或 `insufficient` |
| `summary` | 一句繁體中文摘要；資料不足時說明不足原因 |
| `observations` | 0–3 項，每項皆含 `claim` 與引用具體數字的 `evidence` |
| `suggestions` | 0–2 項、可選擇採納的建議；不使用命令式或醫療判斷 |
| `disclaimer` | 固定說明資料範圍與非評價性質 |

Python 必須處理模型拒答、逾時、不完整回應及 schema 驗證失敗，並只回傳受控錯誤碼給 ASP.NET Core。

## 5. Prompt 規則

System instructions 應要求模型：

- 僅根據輸入 JSON 的數字與日期分析，不得杜撰資料、因果、使用者意圖或未提供的事件。
- 將時間轉為易讀的「小時／分鐘」，但不改變輸入數值的事實意義。
- 至少一項觀察必須有 `evidence`；無法支持的觀察不得輸出。
- 當少於 7 個日資料點、總工時為零或資料明顯不足時，使用 `limited` 或 `insufficient`，且不產生強結論。
- 不對工作品質、人格、心理健康或醫療狀態做判斷。
- 所有文字使用繁體中文，語氣中性、可行且非命令式。

所有百分比、成長率、平均值與排名應優先由 ASP.NET Core 計算並放入 `derivedMetrics`；模型只將已驗證資料轉為可讀敘述。

## 6. 前端體驗

- 在現有 Report 範圍控制項附近加入「分析時間趨勢」按鈕與分析卡片。
- 初始不自動呼叫 AI；只有明確按下按鈕才請求，避免不必要成本。
- 請求中按鈕停用並顯示載入狀態；成功後顯示期間、摘要、觀察、建議與免責說明。
- 改變區間後，舊分析標示為過期並要求重新分析，不能把舊結果當成新區間結果。
- 無資料、資料不足、限流、服務暫時不可用時，各自顯示清楚且不含內部技術細節的訊息。
- 以 `textContent` 建立模型回應文字，不將模型輸出插入 `innerHTML`。
- 手機版分析卡片採單欄，按鈕、載入狀態與錯誤訊息須可鍵盤操作並使用 `aria-live` 宣告狀態。

## 7. 安全、隱私、成本與可靠性

- 新 API 需要 `[Authorize]`，且 POST 必須通過 anti-forgery 驗證。
- 以已登入使用者作為速率限制分割鍵；第一版建議每位使用者每 60 秒最多 1 次，並限制同一使用者同時只有一個分析請求。
- ASP.NET Core 對 Python 設 20 秒逾時；Python 對 OpenAI 設更短或相同的明確逾時，取消請求必須往下游傳遞。
- 預期失敗以 Problem Details 回應：`AI_ANALYSIS_RATE_LIMITED` 為 429、`AI_ANALYSIS_UNAVAILABLE` 為 503、`AI_ANALYSIS_TIMEOUT` 為 504。不得把 OpenAI 的原始錯誤、金鑰或 prompt 回傳給瀏覽器。
- 日誌只記錄受控事件（例如結果狀態、區間長度、延遲、模型識別碼、錯誤碼）；不記錄 API 金鑰、Cookie、使用者輸入原文或完整彙總 payload。
- 預設不保存分析結果。若未來要快取，快取鍵必須含使用者、資料範圍與資料版本，且需先定義保留期限與刪除行為。

## 8. 測試與驗收

### 8.1 ASP.NET Core

- Report 趨勢服務使用 `TimeAggregationService` 的每日、分類與標籤結果，且只讀取目前使用者資料。
- Today、Week、Month、自訂區間、無效 preset、反向日期與部分日期的行為與既有 Report 一致。
- 跨日與 DST 案例確認 `DailyTotals` 與既有 aggregation 測試結果相同。
- Payload 不含 UserId、email、TimeEntry 名稱、原始逐筆時間、Cookie 或 API 金鑰。
- 未登入、缺少 anti-forgery、冷卻中、Python 逾時與 Python 非預期回應都有正確 HTTP 狀態與安全訊息。

### 8.2 Python

- DTO schema 驗證拒絕不合法日期、負數秒數、過大陣列與未知欄位。
- 以 mock OpenAI client 驗證 Structured Output 成功、拒答、逾時、網路錯誤與無法解析 schema 的處理。
- 測試保證 Python 不需要 MySQL 連線字串，也不讀取 JWT。

### 8.3 UI

- 點擊後顯示載入、成功、資料不足、429、503、504 狀態。
- 切換日期區間後，舊結果不會被誤標為新期間的分析。
- 桌面與 320px 寬度下沒有水平溢位，鍵盤與螢幕閱讀器可辨識按鈕與狀態。

## 9. 實作前決策

實作開始前需確認：

1. Python 服務的正式部署方式與專用 systemd service 名稱。
2. 使用的 OpenAI 模型與每次分析可接受的成本/延遲預算。
3. 初版是否完全不保存分析結果（本規格的預設），或是否需要短期快取。
4. 目前定義的「只用彙總資料」是否維持；若要傳送 TimeEntry 名稱，必須另行做隱私、prompt injection 與資料保留設計。

