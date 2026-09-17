# Error Handling and Logging（第 15 項）實作紀錄

依照 [`Docs/superpowers/specs/2026-09-17-error-handling-and-logging-design.md`](superpowers/specs/2026-09-17-error-handling-and-logging-design.md) 與 [`Docs/superpowers/plans/2026-09-17-error-handling-and-logging.md`](superpowers/plans/2026-09-17-error-handling-and-logging.md) 實作，這裡記錄實作時的具體事實、跟計畫不同之處，以及過程中抓到的兩個 framework 層級地雷。

## 設計思路

既有的 Result/ErrorCode 對應（400/401/404/409/429）完全不動，這次只補「沒人預期到會發生」的例外：一個全域 `IExceptionHandler` 攔截所有未處理例外，`/api/*` 回安全的 RFC 7807 JSON，其餘路徑安全地渲染 `/Home/Error`，兩者都帶同一個 `traceId` 可以回頭在 Console/systemd journal 對應到完整例外紀錄。Timer 交易與 CSV 匯出各自的「非預期失敗」（不是已知的 `DbUpdateConcurrencyException`／取消）額外記錄一筆安全的操作事件，同一個 trace id 會自動透過 scope 帶入。

## 整體流程

1. `Program.cs` 註冊 `builder.Services.AddProblemDetails();` 與 `builder.Services.AddExceptionHandler<GlobalExceptionHandler>();`，管線改成 `app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandlingPath = "/Home/Error" })`，搬到 `UseHttpsRedirection`/`UseRouting`/`UseRateLimiter`/`UseAuthentication`/`UseAuthorization` 之前，且**不再限定 `!IsDevelopment()`**——每個環境都用同一套安全行為，開發時的除錯改成完全依賴 Console 輸出裡的完整例外堆疊，不透過回應內容洩漏。
2. `GlobalExceptionHandler.TryHandleAsync`：`OperationCanceledException` 直接放行（不記錄、不產生假回應）；其餘例外先用 message-template scope（`TraceId:{TraceId} RequestMethod:{RequestMethod} RequestPath:{RequestPath}`）記一筆 Error log，再依路徑分流——`/api/*` 直接用 `IProblemDetailsService.WriteAsync` 寫固定的安全 JSON（`type`/`title=INTERNAL_SERVER_ERROR`/`detail`＝`AGENTS.md` 規定的「發生錯誤，請稍後再試。」/`traceId`），回傳 `true`；其他路徑把 `traceId` 存進 `HttpContext.Items["TraceId"]` 後回傳 `false`，讓框架原本設定的 `ExceptionHandlingPath` 接手重新執行請求到 `/Home/Error`。
3. `HomeController.Error()` 改讀 `HttpContext.Items["TraceId"]`（找不到才退回 `Activity.Current?.Id ?? TraceIdentifier`），`Views/Shared/Error.cshtml` 精簡成一段安全訊息 + Request ID，移除舊的 Development Mode 說明與任何例外相關內容。
4. `Infrastructure/Logging/IOperationalEventLogger`／`OperationalEventLogger`：`TimerTransactionFailed(userId, operation, exception)`（Event ID 1001）、`CsvExportFailed(userId, hasRange, hasCategoryFilter, hasSearch, exception)`（Event ID 1002），參數都是已經安全的基本型別，呼叫端不可能不小心塞進使用者輸入字串。
5. `TimerService.StopAsync` 把交易包進 `try/catch(Exception exception) when (exception is not DbUpdateConcurrencyException)`：非預期例外記一筆 `TimerTransactionFailed("Stop", ...)`、（若交易已成功開始）rollback、重新拋出讓全域 handler 接手；已知的並發衝突路徑維持原本行為，不記成 Error。`CsvExportService.ExportAsync` 同樣包一層 `try/catch(Exception exception) when (exception is not OperationCanceledException)`，失敗時記 `CsvExportFailed`（用布林值描述有沒有帶 range/category/search，不記實際內容）後重新拋出。

## 跟計畫不同之處（過程中抓到的兩個 framework 地雷）

- **`ExceptionHandlerOptions.ExceptionHandlingPath` 會在呼叫任何註冊的 `IExceptionHandler` 之前，就先把 `HttpContext.Request.Path` 覆寫成設定的路徑**——這是計畫沒有提到、文件也沒明講的框架內部行為。第一版程式碼在 handler 裡直接讀 `httpContext.Request.Path.StartsWithSegments("/api")` 來判斷要不要走 JSON 分支，結果**不管原始請求打的是不是 `/api/*`，這個判斷永遠讀到已經被改寫成 `/Home/Error` 的值**，導致 `/api/test/throw` 也被誤判成非 API 路徑、走 HTML fallback。用一支臨時的 `CapturingLoggerProvider`（見下方）把 handler 內部的每一步記下來才抓到這個現象；正確作法是改讀 `httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path`——這個 feature 在 middleware 改寫 `Request.Path` 前就已經設好，內容是「真正丟例外的原始路徑」，才是這裡該用的值。**教訓：`ExceptionHandlerOptions.ExceptionHandlingPath` 不是單純「一個字串設定」，它會改變 `HttpContext` 本身的可觀察狀態，任何自訂 `IExceptionHandler` 都不能信任 `Request.Path` 反映「原始」請求。**
- **預設 Console formatter 不會印出 `ILogger.BeginScope` 的內容，即使打開 `IncludeScopes`，用一個 `Dictionary<string,object?>` 當 scope state 也只會印出型別名稱字串**，不是想要的 `TraceId=...` 這種可讀格式。要嘛用 message-template 形式的 scope（`logger.BeginScope("TraceId:{TraceId} ...", ...)`，這樣产生的 state 實作 `IReadOnlyList<KeyValuePair<string,object>>` 且 `.ToString()` 會正確渲染），要嘛自己寫 formatter。已改用 message-template scope，並在 `Program.cs` 用 `builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);` 明確開啟（單純在 `appsettings.json` 寫 `Logging:Console:FormatterOptions:IncludeScopes` 沒有生效，原因未深究，程式碼內顯式設定比較保險，兩者都留著）。
- **計畫要求的 `Docs/HANDOFF.md`「Modify after verification only」順序已遵守**：只有在四個自動化測試專案全過、`git diff --check` 確認過（唯一的警告是 `HomeController.cs` 本來就是 CRLF 檔案、`core.autocrlf=false` 造成的既有現象，不是這次改動引入的新問題）、且用 `ASPNETCORE_ENVIRONMENT=Testing` 打過真正的 Kestrel/Console 輸出、再用 `ASPNETCORE_ENVIRONMENT=Development` 接真正的 MySQL 資料庫跑過一輪已知業務錯誤（404/400）與正常 Start/Stop Timer 之後，才回來更新 `Docs/TODO.md`/`Docs/HANDOFF.md`。

## 檔案清單

| 檔案 | 內容 |
|---|---|
| `Infrastructure/Errors/GlobalExceptionHandler.cs` | 唯一的 `IExceptionHandler` 實作：`/api/*` 寫安全 Problem Details，其餘路徑記錄 `traceId` 後放行給框架的 `ExceptionHandlingPath` |
| `Infrastructure/Logging/IOperationalEventLogger.cs` / `OperationalEventLogger.cs` | Timer/CSV 非預期失敗的安全事件記錄，Event ID 1001/1002 |
| `Program.cs` | `AddProblemDetails()`、`AddExceptionHandler<GlobalExceptionHandler>()`、`AddScoped<IOperationalEventLogger, OperationalEventLogger>()`、`AddSimpleConsole(IncludeScopes=true)`、管線順序調整、`Testing` 環境下略過 MySQL `AddDbContext`（讓 `WebFlow.Tests` 的 `WebApplicationFactory` 能乾淨地換成 SQLite）、`Testing` 環境專用的 `/api/test/throw`、`/test/throw` 測試端點、檔尾 `public partial class Program;`（讓 `WebApplicationFactory<Program>` 可以 host 這個 top-level-statement 專案） |
| `Controllers/HomeController.cs` | `Error()` 改讀 `HttpContext.Items["TraceId"]` |
| `Views/Shared/Error.cshtml` | 精簡成安全訊息 + Request ID，移除 Development Mode 說明 |
| `Services/TimerService.cs` | `StopAsync` 交易包一層非預期例外的 catch/rollback/rethrow，記 `TimerTransactionFailed` |
| `Services/CsvExportService.cs` | `ExportAsync` 包一層非預期例外的 catch/rethrow，記 `CsvExportFailed` |
| `appsettings.json` | 新增 `Logging:Console:FormatterOptions:IncludeScopes`（宣告式保留，實際生效靠 `Program.cs` 的 `AddSimpleConsole`） |
| `Tests/WebFlow.Tests/WebFlow.Tests.csproj` | 新測試專案，`Microsoft.AspNetCore.Mvc.Testing` + SQLite（跟其他測試專案一致的 provider 選擇準則） |
| `Tests/WebFlow.Tests/CustomWebApplicationFactory.cs` | `WebApplicationFactory<Program>`：env vars 提供假的 connection string/JWT signing key（讓 `Program.cs` 的啟動守衛通過）、`Testing` 環境、SQLite shared-cache in-memory DbContext、內建 `CapturingLoggerProvider` |
| `Tests/WebFlow.Tests/CapturingLoggerProvider.cs` | 純記憶體 `ILoggerProvider`，斷言 level/EventId/訊息/例外/結構化 state |
| `Tests/WebFlow.Tests/ErrorHandlingTests.cs` | 9 個測試，見下方 |
| `Tests/TimerFlow.Tests/Integration/TimerServiceTests.cs` | `CreateService` 改傳入 `NoopOperationalEventLogger`（`new OperationalEventLogger(NullLogger<OperationalEventLogger>.Instance)`） |
| `Tests/TimeEntryFlow.Tests/Integration/CsvExportServiceTests.cs` | 同上，`CreateService` 多帶一個 no-op logger |

## 測試

`Tests/WebFlow.Tests`（9 個，皆用 `IClassFixture<CustomWebApplicationFactory>` 或個別隔離的 factory 實例）：

1. `Throwing_api_endpoint_returns_safe_problem_details_with_trace_id` / `Throwing_html_endpoint_renders_safe_error_page_with_trace_id`：各自用獨立的 factory（避免跟其他測試共用同一份 log 歷史造成斷言歧義），驗證狀態碼、`Content-Type`、body 不含例外文字、`traceId` 非空，並確認 `CapturingLoggerProvider` 真的記到一筆 Error 等級的 `Unhandled request exception.`。
2. `TimerService_unexpected_failure_logs_safe_metadata_and_rethrows` / `CsvExportService_unexpected_failure_logs_safe_metadata_and_rethrows`：白箱測試，不透過 HTTP——直接建構 service，在呼叫前 `dbContext.DisposeAsync()` 讓任何後續 DB 呼叫確定丟 `ObjectDisposedException`，斷言例外重新拋出、`OperationalEventLogger` 記到安全 metadata（`UserId`/`Operation` 或 `HasRange`/`HasCategoryFilter`/`HasSearch`），且訊息與結構化 state 都不含刻意塞入的「secret」搜尋字串。
3. `TimerService_known_concurrency_outcome_is_not_logged_as_an_operational_error`：沒有正在執行的計時器時呼叫 `StopAsync`，確認回傳 `TIMER_NOT_RUNNING` 且 `OperationalEventLogger` 完全沒有記錄 Event ID 1001。
4. `Csv_export_with_an_invalid_range_still_returns_400` / `Csv_export_without_authentication_still_returns_401` / `Timer_stop_with_no_running_timer_still_returns_404`：既有業務錯誤回應維持不變。
5. `Rate_limit_rejection_still_returns_429`：用獨立的 factory 連續打 11 次 `POST /Account/Login`（壞密碼），確認第 11 次回 429 `TOO_MANY_REQUESTS`——刻意不跟共用 fixture 混用，因為速率限制的分區鍵是 IP，跟其他測試共用會互相干擾配額。

`Tests/TimerFlow.Tests`（10 個，含既有的並發 Stop 測試，改動後重跑全過，確認交易重構沒有破壞既有行為）、`Tests/TimeEntryFlow.Tests`（105 個）、`Tests/AuthFlow.Tests`（12 個）皆維持全過。四個 C# 測試專案合計 136 個測試。

## 手動驗證

1. `ASPNETCORE_ENVIRONMENT=Testing`（`--no-launch-profile` 略過 `launchSettings.json`，`Jwt__SigningKey` 用環境變數帶一個假值，因為 user-secrets 只在 Development 載入）跑真正的 Kestrel：打 `GET /api/test/throw` 確認回應是 `500` + `application/problem+json` + 不含 `secret exception text` + `traceId` 非空；打 `GET /test/throw` 確認回應是 `500` + `text/html` + 含 Request ID + 不含 Development Mode 說明；比對 Console 輸出，確認 `GlobalExceptionHandler` 的 Error log 的 scope 正確顯示原始路徑（不是被覆寫過的 `/Home/Error`）且其中的 `TraceId` 跟回應裡的 `traceId` 完全一致。
2. `ASPNETCORE_ENVIRONMENT=Development` 接真正的 MySQL 開發資料庫：註冊帳號（確認 `Registration succeeded` 仍照原本層級記錄）→ 打 `POST /api/timer/stop`（沒有計時器）確認仍是 `404 TIMER_NOT_RUNNING`、`GET /api/time-entries/export` 帶不合法範圍確認仍是 `400 INVALID_TIME_RANGE`（兩者現在都額外帶一個 `traceId`，是 `AddProblemDetails()` 全站生效的附帶效果，不是刻意加的）→ 走一次真正的 Start/Stop Timer 全流程確認 `TimerService.StopAsync` 交易重構後在真正的 MySQL 上仍然正常建立 TimeEntry。驗證用的測試資料（TimeEntry、帳號）已於驗證後刪除。

## 尚未涵蓋的部分

`Infrastructure/Logging` 目前只有 auth 事件（`IAuthEventLogger`）與這次新增的 Timer/CSV 操作事件（`IOperationalEventLogger`）。設計文件表格裡列的「Database-related unexpected failure」沒有獨立成一個事件——這類失敗多半會從 Service 方法（`TimeEntryService`/`CategoryService`/`TagService` 等）的一般資料庫操作中冒出來，目前**完全交給 `GlobalExceptionHandler` 的通用 Error log（含 `method`/`path`/exception）承接**，沒有另外包一層 try/catch 加 DB 專屬的結構化欄位——這符合設計文件「Database-related unexpected failure | trace ID, method, path, exception object」剛好就是 `GlobalExceptionHandler` 已經記的內容，不需要重複。啟動/關閉的 `Information` 等級記錄沿用 ASP.NET Core 內建的 `Microsoft.Hosting.Lifetime` 記錄，沒有另外疊加自訂事件。
