# OpenAI 時間趨勢分析：實作與 EC2 部署紀錄

**完成日期：** 2026-09-28  
**功能狀態：** 已實作、已部署、已完成真實 OpenAI 端對端驗證  
**目前公開傳輸狀態：** 暫時 HTTP demo；尚未設定 HTTPS

## 1. 功能範圍

Report 頁面新增「分析時間趨勢」按鈕。使用者主動觸發後，系統以目前選取區間的彙總資料產生繁體中文摘要、觀察與建議。

不傳送 TimeEntry 名稱、描述、email、UserId、Cookie、原始逐筆 UTC 時間或資料庫連線資訊給 Python 或 OpenAI。模型不負責重算工時；日期區間、時區、跨日切分、分類與標籤彙總仍完全由 ASP.NET Core 的 `TimeAggregationService` 處理。

## 2. 架構

```text
Browser
  | POST /api/reports/trend-analysis
  | JWT Cookie + X-CSRF-TOKEN
  v
ASP.NET Core
  | CurrentUserService 取得登入使用者
  | ReportService / TimeAggregationService 產生最小化統計 DTO
  | HTTP + X-AI-Internal-Token (127.0.0.1 only)
  v
Python FastAPI: ltm-ai-trend
  | OPENAI_API_KEY
  v
OpenAI Responses API + Structured Outputs
```

Python service 沒有 MySQL 連線字串、JWT 驗證或瀏覽器 Cookie 存取權。Nginx 不代理 port 8011，EC2 對外也不開放此 port。

## 3. 主要實作檔案

| 路徑 | 職責 |
| --- | --- |
| `OpenAiTrendAnalysis/app/main.py` | FastAPI internal endpoint、Pydantic input validation、OpenAI Responses API、Structured Output response schema |
| `OpenAiTrendAnalysis/tests/test_main.py` | Python unit / route tests，以 mock OpenAI client 驗證，不耗用 API 額度 |
| `Controllers/Api/TrendAnalysisApiController.cs` | `[Authorize]`、`[ValidateAntiForgeryToken]`、AI rate limit 與 Problem Details 映射 |
| `Services/TrendAnalysisService.cs` | 組合 Report input 與 Python client |
| `Services/TrendAnalysisClient.cs` | loopback HTTP、內部 token、20 秒 timeout 與安全錯誤映射 |
| `Services/ReportService.cs` | 產生每日、分類、標籤與確定性衍生指標；限制最大 365 天 |
| `wwwroot/js/report.js` | Report 按鈕、CSRF fetch、載入／錯誤／安全 DOM rendering |

### 3.1 API 與限制

```text
POST /api/reports/trend-analysis
```

- 只允許已登入使用者，並驗證 anti-forgery token。
- 每位使用者每 60 秒最多一次。
- 自訂期間最多 365 天；分類與標籤各最多送前 10 名。
- Python service error 映射為 `AI_ANALYSIS_RATE_LIMITED` (429)、`AI_ANALYSIS_UNAVAILABLE` (503)、`AI_ANALYSIS_TIMEOUT` (504)。
- 前端以 `textContent` 顯示模型輸出，不能以 `innerHTML` 插入模型文字。

## 4. Python endpoint 修正紀錄

首次部署後，Browser → ASP.NET Core → Python 的請求回傳 503；Python journal 顯示 422。原因是 endpoint 原先將 `require_internal_token` 當成普通函式預設值，FastAPI 將它解析成請求參數而非 dependency。

修正為：

```python
def analyze_time_trend(
    payload: TrendAnalysisInput,
    _: None = Depends(require_internal_token),
) -> TrendAnalysis:
```

並新增 HTTP route test，確認未帶 `X-AI-Internal-Token` 的請求會回 401。修正後以 Python-only release `ltm-ai-trend-20260928-r1` 重部署，實際按下 Report 分析按鈕已成功取得 OpenAI 結果。

## 5. 本機驗證

執行位置：`OpenAiTrendAnalysis/`。

```powershell
..\.venv\Scripts\python.exe -m unittest discover -s .\tests -v
```

2026-09-28 結果：6/6 通過。測試使用 mock OpenAI client，不會送出網路請求或耗用 API 額度。

ASP.NET Core `dotnet build --no-restore` 成功，`TimeEntryFlow.Tests` 105/105 通過。`WebFlow.Tests` 在本機 Windows 因 `.NET Runtime` Windows Event Log 權限而失敗，並非 AI 功能編譯失敗；部署前後的實際 Report AI 整合已在 EC2 成功驗證。

## 6. EC2 目前部署配置

### 6.1 Release 位置

```text
/srv/ltm/app/current
  -> /srv/ltm/releases/ltm-openai-20260928

/srv/ltm/ai-trend/current
  -> /srv/ltm/ai-trend/releases/ltm-ai-trend-20260928-r1

/srv/ltm/ai-trend/venv
```

舊 ASP.NET Core release `http-demo-20260917` 與初版 Python release `ltm-ai-trend-20260928` 都保留作為回滾來源。

### 6.2 Python systemd unit

`/etc/systemd/system/ltm-ai-trend.service`：

```ini
[Unit]
Description=LTM OpenAI time trend analysis service
After=network-online.target
Wants=network-online.target

[Service]
Type=exec
User=ltm
Group=ltm
WorkingDirectory=/srv/ltm/ai-trend/current
EnvironmentFile=/etc/ltm/ltm-ai-trend.env
ExecStart=/srv/ltm/ai-trend/venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8011
Restart=on-failure
RestartSec=5
TimeoutStopSec=30
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true
ReadWritePaths=/srv/ltm

[Install]
WantedBy=multi-user.target
```

常用檢查：

```bash
sudo systemctl status ltm-ai-trend --no-pager
curl --fail http://127.0.0.1:8011/health
sudo ss -ltnp | grep ':8011'
sudo journalctl -u ltm-ai-trend -n 100 --no-pager
```

預期 socket 為 `127.0.0.1:8011`，不可是 `0.0.0.0:8011`。

## 7. 私密設定

### 7.1 Python：`/etc/ltm/ltm-ai-trend.env`

檔案需由 `root:ltm` 擁有，mode `0640`：

```text
OPENAI_API_KEY=<OpenAI API key>
AI_TREND_INTERNAL_TOKEN=<random 32-byte hex token>
OPENAI_MODEL=gpt-6-luna
```

### 7.2 ASP.NET Core：`/etc/ltm/ltm.env`

保留既有資料庫與 JWT 設定，另新增：

```text
AiTrendAnalysis__InternalToken=<same random token as Python>
AiTrendAnalysis__BaseUrl=http://127.0.0.1:8011/
AiTrendAnalysis__RequestTimeoutSeconds=20
```

不得將 API key、internal token 或這兩份環境檔提交到 Git、複製到 Nginx 設定、列印到日誌或貼到 issue/chat。

## 8. 未來 Python-only 更新流程

1. 本機執行 Python tests，並建立新的 zip，例如 `ltm-ai-trend-YYYYMMDD-rN.zip`。
2. 上傳 zip 到 EC2 `/tmp/`。
3. 解壓到 `/srv/ltm/ai-trend/releases/<release-id>/`，然後 `chown -R ltm:ltm`。
4. 將 `/srv/ltm/ai-trend/current` 改指向新目錄。
5. `sudo systemctl restart ltm-ai-trend`。
6. 依序檢查 systemd status、`/health`、loopback binding，再由 Report 頁面驗證。

ASP.NET Core-only 更新沿用既有 `/srv/ltm/releases/<release-id>` 與 `/srv/ltm/app/current` symlink 流程；本功能沒有 EF migration，因此不需為 AI feature 執行 `database update`。

## 9. 回滾

若 Python 更新失敗，將 `/srv/ltm/ai-trend/current` 指回已知正常的 Python release，然後重啟 `ltm-ai-trend`。若 ASP.NET Core 更新失敗，將 `/srv/ltm/app/current` 指回 `http-demo-20260917`，再重啟 `ltm`。

保留舊 release，直到新版本穩定且已完成 Report 真實請求驗證；不要以 `rm -rf` 立即刪除回滾來源。

## 10. HTTP 暫時模式警告

目前 `/etc/ltm/ltm.env` 保持：

```text
Demo__AllowInsecureHttp=true
```

這是使用者明確選擇的暫時 demo 設定。它會停用 HTTPS redirect / HSTS 並讓 auth cookie 不要求 `Secure`。雖然 `OPENAI_API_KEY` 不會傳到瀏覽器，但登入資料與 Report 資料的瀏覽器傳輸未加密。

正式對外使用前必須：設定網域與 TLS 憑證、確認 Nginx 的 443 reverse proxy、將 `Demo__AllowInsecureHttp` 改為 `false`，並重新驗證登入與 Report AI 分析。
