# Login/Register Rate Limiting 與 Auth 事件記錄實作說明

本文件說明 Login/Register rate limiting 與 `Infrastructure/Logging` 的 auth failure 記錄如何實作，包含設計思路、牽涉的檔案，以及每個檔案負責的內容。對應 `AGENTS.md` Security Requirements 的「Login/Register should use basic rate limiting」與 Logging 章節的「authentication failure summary」，也是 [`Docs/JWT.md`](JWT.md) 结尾列出的「尚未涵蓋的部分」中的兩項。

## 設計思路

- **Rate limiting 採用 ASP.NET Core 內建的 `Microsoft.AspNetCore.RateLimiting` middleware**，不引入額外套件。這個 middleware 從 .NET 7 開始就包含在共用框架（`Microsoft.NET.Sdk.Web`）內。
- **以來源 IP 做為 partition key、Fixed Window 演算法**：同一個 IP 在 5 分鐘內最多 10 次請求（`PermitLimit = 10`、`Window = 5 分鐘`、`QueueLimit = 0`，超過直接拒絕不排隊）。Login 與 Register 共用同一個具名 policy（`RateLimiterPolicies.Auth`），因為兩者都是未登入即可呼叫的端點，攻擊者可能交替嘗試，共用同一個桶子才能真正限制住單一來源的總嘗試次數。
- **只做「basic」限制，不做進階策略**：沒有依 Email 做第二層限制、沒有滑動視窗、沒有 Redis 分散式儲存。因為 V1 是單一 EC2 執行個體、單人使用的小型專案（見 `AGENTS.md` Non-goals：不使用 Redis），記憶體內的 Fixed Window Limiter 已足夠。
- **超過限制時回傳統一格式的 429**：呼叫端已有的 `Problem(...)` 慣例是回傳 `{ title, status, detail }`，因此 `OnRejected` 回呼中手動組出相同形狀的 JSON（`title = "TOO_MANY_REQUESTS"`），前端可以用同一套錯誤處理邏輯解析。
- **Auth 事件記錄集中在 `Infrastructure/Logging/IAuthEventLogger`**，而不是讓 `AuthService`/`AccountController` 直接呼叫 `ILogger`：
  - 統一記錄格式（Email、ErrorCode、IpAddress 三個結構化欄位），避免各處寫法不一致。
  - `AGENTS.md` 明確禁止記錄密碼、JWT、Cookie；把記錄邏輯收斂到一個型別，之後審查「有沒有不小心記錄到敏感資料」時只需要看這一個檔案。
  - 只記錄「摘要」等級的資訊（Email + 失敗原因代碼 + IP），不記錄密碼雜湊、逐字錯誤堆疊等細節，符合「authentication failure summary」的字面意思。
- **IP 位址由 Controller 讀取、往下傳給 Service**：`HttpContext` 屬於 Request 的一部分，讀取它是 Controller 的職責（`AGENTS.md`：Controller 負責 Receive request），`AuthService` 保持不直接依賴 `HttpContext`/`IHttpContextAccessor`，維持 Service 可以脫離 ASP.NET Core pipeline 被單元測試呼叫。

## 整體流程

```text
1. 使用者呼叫 POST /Account/Login 或 /Account/Register

2. Middleware pipeline 依序執行：
   UseRouting → UseRateLimiter → UseAuthentication → UseAuthorization
   → 若同一 IP 在 5 分鐘內已達 10 次，UseRateLimiter 直接短路，
     觸發 OnRejected：記錄 RateLimitExceeded 並回傳 429 JSON，
     不會進入 Controller。

3. 未被擋下的請求進入 AccountController：
   → 讀取 HttpContext.Connection.RemoteIpAddress
   → 呼叫 AuthService.LoginAsync/RegisterAsync(email, password, ipAddress, ct)

4. AuthService 在每個成功/失敗分支呼叫 IAuthEventLogger：
   → 成功：LoginSucceeded / RegisterSucceeded（Information 等級）
   → 失敗（帳號不存在、密碼錯誤、Email 重複）：
     LoginFailed / RegisterFailed（Warning 等級，附上 ErrorCode）

5. AuthEventLogger 透過 ILogger<AuthEventLogger> 寫出結構化 log，
   最終依 appsettings.json 的 Logging 設定輸出到 Console/其他 Provider。
```

## 檔案清單與內容

### `Infrastructure/Logging/`

| 檔案 | 內容 |
|---|---|
| `IAuthEventLogger.cs` | 定義五個方法：`LoginSucceeded`/`LoginFailed`/`RegisterSucceeded`/`RegisterFailed`/`RateLimitExceeded`，每個都只接受 Email、ErrorCode、Endpoint、IpAddress 這類非敏感欄位。 |
| `AuthEventLogger.cs` | 實作上述介面，包住 `ILogger<AuthEventLogger>`。成功事件用 `LogInformation`，失敗/rate limit 事件用 `LogWarning`，訊息皆為結構化模板（`{Email}`、`{ErrorCode}`、`{IpAddress}` 等具名欄位），方便之後接上結構化 log 蒐集系統做查詢/告警。 |

### `Services/AuthService.cs`

- 建構子新增 `IAuthEventLogger authEventLogger` 相依性。
- `RegisterAsync`/`LoginAsync` 都新增 `string? ipAddress` 參數，並在既有的每一個 return 分支前呼叫對應的 `authEventLogger` 方法（包含 `DbUpdateException` catch 區塊裡「併發重複註冊」的失敗分支）。
- 商業邏輯本身（密碼驗證、Email 正規化、重複檢查）未變動。

### `Controllers/AccountController.cs`

- `Login`/`Register` 兩個 `POST` action 加上 `[EnableRateLimiting(RateLimiterPolicies.Auth)]`（`Microsoft.AspNetCore.RateLimiting` 提供的 attribute，讀取 `Program.cs` 註冊的具名 policy）。`Logout`（需要先登入才能呼叫）不受限制。
- 呼叫 `AuthService` 時，多傳入 `HttpContext.Connection.RemoteIpAddress?.ToString()` 作為 `ipAddress` 引數。

### `Security/RateLimiterPolicies.cs`

- 新增檔案，集中定義 policy 名稱常數 `RateLimiterPolicies.Auth = "auth"`，比照既有 `AuthConstants.cs` 把「魔法字串」集中管理的作法，避免 `Program.cs` 與 `AccountController.cs` 兩處手動輸入字串不一致。

### `Program.cs`

- 新增 `builder.Services.AddSingleton<IAuthEventLogger, AuthEventLogger>()`（無狀態，註冊為 Singleton 即可，与既有的 `JwtTokenService` 一致）。
- 新增 `builder.Services.AddRateLimiter(...)`：
  - `RejectionStatusCode = 429`。
  - `AddPolicy(RateLimiterPolicies.Auth, ...)`：以 `RateLimitPartition.GetFixedWindowLimiter`，partition key 為來源 IP（無法取得時退回 `"unknown"`，避免例外），`PermitLimit = 10`、`Window = 5 分鐘`、`QueueLimit = 0`。
  - `OnRejected`：從 `HttpContext.RequestServices` 解析 `IAuthEventLogger` 並呼叫 `RateLimitExceeded`，接著回傳 `application/problem+json` 格式的 429 回應。
- Middleware pipeline 新增 `app.UseRateLimiter()`，放在 `app.UseRouting()` 之後、`app.UseAuthentication()` 之前——因為 rate limiting 應該儘早短路惡意請求，不需要等到驗證/授權階段才擋下。

### `Tests/AuthFlow.Tests/Integration/AuthServiceTests.cs`

- 新增內部測試替身 `NoOpAuthEventLogger`（實作 `IAuthEventLogger`，五個方法皆為空實作），讓既有的 12 個測試不需要驗證記錄行為，只需要滿足建構子相依性即可編譯執行。
- 既有測試呼叫 `RegisterAsync`/`LoginAsync` 的地方，統一補上 `ipAddress: null` 引數（測試情境沒有真實的 HTTP 連線，`null` 等同「非 HTTP 呼叫方」）。
- 當時未新增額外的 rate limiting 測試：`AddRateLimiter`/`UseRateLimiter` 屬於 ASP.NET Core middleware pipeline 的行為，需要 `WebApplicationFactory` 等級的整合測試才有意義，`AuthServiceTests` 是直接建構 `AuthService` 的單元/整合測試，沒有經過 middleware，不適合驗證這塊。**（後續 session 更新，已完成）** Error handling/Logging（第 15 項）新增了 `Tests/WebFlow.Tests`（第一個 `WebApplicationFactory<Program>` 測試專案），其中 `Rate_limit_rejection_still_returns_429` 連打 11 次壞密碼登入，驗證第 11 次確實回 429 `TOO_MANY_REQUESTS`，細節見 [`Docs/ErrorHandlingAndLogging.md`](ErrorHandlingAndLogging.md)。

## 尚未涵蓋的部分

- **Rate limiting 沒有針對「同一 Email、不同 IP」的情境做限制**（例如透過大量 Proxy 輪流嘗試同一帳號的密碼），僅依 IP 做限制。V1 範圍內視為可接受的取捨。
- ~~`Infrastructure/Logging` 目前只有 Auth 相關事件~~ **（後續 session 更新，已完成）**：第 15 項新增 `IOperationalEventLogger`（timer transaction failure、CSV export failure），沿用這裡 `IAuthEventLogger` 的模式（介面 + 結構化欄位 + 明確排除敏感欄位）；未預期例外的通用 Error log（含 DB 錯誤）改由新增的 `GlobalExceptionHandler` 統一記錄。startup/shutdown 沿用 ASP.NET Core 內建的 `Microsoft.Hosting.Lifetime` 記錄，沒有另外疊加自訂事件。細節見 [`Docs/ErrorHandlingAndLogging.md`](ErrorHandlingAndLogging.md)。
