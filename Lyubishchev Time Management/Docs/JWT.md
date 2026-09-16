# JWT Cookie 驗證流程實作說明

本文件說明 `feat/jwt-cookie-auth-flow` 分支（已合併進 `main`）如何實作 JWT 驗證，包含設計思路、牽涉的檔案，以及每個檔案負責的內容。對應 `AGENTS.md` Implementation Order 的第 2 項「User / Register / Login / Logout / JWT」。

## 設計思路

- **JWT 存放在 Secure + HttpOnly Cookie，而非 `Authorization` Header**：符合 `AGENTS.md` 的 Security Requirements。瀏覽器端 JS 完全碰不到 token，可降低 XSS 竊取 token 的風險。
- **驗證機制沿用 `Microsoft.AspNetCore.Authentication.JwtBearer`，但改寫 token 來源**：JwtBearer 預設從 `Authorization: Bearer <token>` header 讀取 token；這裡透過 `JwtBearerEvents.OnMessageReceived` 改成從 Cookie 讀取，這樣就能同時滿足「JWT 簽發/驗證邏輯」與「Cookie 存放」兩個需求，不需要另外實作一套 Cookie Authentication Handler。
- **因為 Cookie 是瀏覽器自動夾帶的，所以需要額外的 CSRF 防護**：所有會改變狀態的 `POST`（Login/Register/Logout）都搭配 ASP.NET Core 內建的 Antiforgery，並改用 Header 傳遞 token（因為前端是用 `fetch` 送 JSON，不是傳統表單），而不是預設的隱藏欄位。
- **未登入使用者存取私人頁面時，導回登入頁而非直接回傳 401**：MVC 頁面（如 `/Dashboard`）與未來的 `/api/*` JSON 端點需要不同的「驗證失敗」處理方式，因此在 `OnChallenge` 事件中依路徑前綴分流。
- **密碼雜湊採用 ASP.NET Core 內建的 `PasswordHasher<TUser>`**（PBKDF2），不用外部套件，因為專案 SDK 是 `Microsoft.NET.Sdk.Web`，此類別已包含在共用框架內。

## 整體流程

```text
1. 使用者於 /Account/Login 或 /Account/Register 取得頁面
   → _AuthLayout.cshtml 呼叫 IAntiforgery.GetAndStoreTokens，
     於 <meta name="csrf-token"> 輸出 request token，
     同時在回應中設定 antiforgery cookie

2. 前端 JS (auth.js) 送出表單時：
   → fetch POST /Account/Login 或 /Account/Register
   → Header 附上 X-CSRF-TOKEN（讀取 meta tag）
   → Body 為 JSON { email, password, (confirmPassword) }

3. AccountController 呼叫 AuthService：
   → Register：檢查 Email 是否重複 → 建立 User → 雜湊密碼 → 存 DB
   → Login：查詢 User → 驗證密碼雜湊
   → 成功後皆呼叫 JwtTokenService.CreateToken() 產生 JWT

4. AccountController 將 JWT 寫入 Cookie（ltm_auth）
   → HttpOnly + Secure + SameSite=Strict + 8 小時效期
   → 回傳 JSON { redirectUrl } 讓前端導頁（不用整頁 reload 的 redirect）

5. 之後每個請求，JwtBearer 的 OnMessageReceived 會自動從 Cookie
   讀出 token 並驗證簽章/Issuer/Audience/效期

6. [Authorize] 的 Controller（Dashboard、TimeEntry）：
   → 驗證成功 → CurrentUserService 從 Claims 取出 UserId
   → 驗證失敗（未帶 token / token 過期）→ OnChallenge 導向
     /Account/Login?returnUrl=<原始路徑>（API 路徑則回 401）

7. 登出：POST /Account/Logout（需已登入 + CSRF token）
   → 清除 ltm_auth Cookie → 導回登入頁
```

## 檔案清單與內容

### `Security/`

| 檔案 | 內容 |
|---|---|
| `JwtOptions.cs` | 對應 `appsettings.json` 的 `Jwt` 區段（`Issuer`、`Audience`、`SigningKey`、`ExpirationHours`），供 DI 綁定使用。 |
| `JwtTokenService.cs` | `CreateToken(User user)`：組出 `sub`（UserId）、`email`、`jti`、`iat` claims，用 `HmacSha256` + `SigningKey` 簽章，回傳序列化後的 JWT 字串。 |
| `CurrentUserService.cs` | `GetRequiredUserId()`：從 `HttpContext.User` 讀出目前登入者的 UserId（同時相容 `ClaimTypes.NameIdentifier` 與原始的 `sub`，因為新版 `JwtBearer` 預設的 `JsonWebTokenHandler` 不會做 inbound claim type 轉換）。之後 Controller/Service 若要取得「目前使用者」都應該透過這個服務，不可信任前端傳入的 UserId。 |
| `AuthConstants.cs` | 集中定義 Cookie 名稱（`ltm_auth`）與效期（8 小時），避免魔法字串散落各處。 |
| `ReturnUrlPolicy.cs` | `IsSafeLocalUrl(string? returnUrl)`：只接受以 `/` 開頭、且不是 `//` 或 `/\` 開頭的路徑，避免登入後導頁被用來做 Open Redirect 攻擊。用於 `OnChallenge` 產生的 `returnUrl` 以及 `AccountController.Login` 收到的 `returnUrl` query string。 |

### `Services/`

| 檔案 | 內容 |
|---|---|
| `AuthService.cs` | 應用層服務，包住實際的註冊/登入商業邏輯：`RegisterAsync`（Email 正規化、重複檢查、`PasswordHasher<User>` 雜湊、寫入 `AppDbContext`）與 `LoginAsync`（查詢使用者、驗證密碼雜湊）。兩者成功時都呼叫 `JwtTokenService` 簽發 token，並回傳統一的 `AuthResult`（成功/失敗、errorCode、錯誤訊息），讓 Controller 可以直接對應到正確的 HTTP 狀態碼。 |

### `Controllers/`

| 檔案 | 內容 |
|---|---|
| `AccountController.cs` | `GET Login`/`GET Register`：回傳頁面。`POST Login`/`POST Register`：接收 JSON body，呼叫 `AuthService`，成功則寫入 Cookie 並回傳 `{ redirectUrl }`，失敗回傳對應狀態碼（401/409）與 `ProblemDetails`。`POST Logout`：需 `[Authorize]`，清除 Cookie 並導回登入頁。三個 POST 皆標註 `[ValidateAntiForgeryToken]`。 |
| `DashboardController.cs` / `TimeEntryController.cs` | 加上 `[Authorize]`，未登入會被 JwtBearer 的 `OnChallenge` 導向登入頁。這是這次補上的最主要的安全缺口——先前這兩個頁面完全公開。 |

### `Program.cs`

- 讀取並驗證 `Jwt:SigningKey`（缺少則直接拋例外中止啟動，比照既有的 `ConnectionStrings:DefaultConnection` 檢查模式）。
- 註冊 DI：`JwtOptions`（`Configure<JwtOptions>`）、`JwtTokenService`（Singleton）、`CurrentUserService`（Scoped）、`AuthService`（Scoped）。
- `AddAntiforgery`：設定 `HeaderName = "X-CSRF-TOKEN"`。
- `AddAuthentication().AddJwtBearer(...)`：
  - `TokenValidationParameters`：驗證 Issuer/Audience/簽章/效期。
  - `OnMessageReceived`：從 `ltm_auth` Cookie 取出 token。
  - `OnChallenge`：`/api` 開頭的路徑維持預設 401；其餘頁面請求導向 `/Account/Login?returnUrl=...`。
- Middleware pipeline 加入 `app.UseAuthentication()`（必須在 `UseAuthorization()` 之前）。

### `Views/Shared/`

| 檔案 | 內容 |
|---|---|
| `_Layout.cshtml` | 加入 `<meta name="csrf-token">`（透過 `IAntiforgery.GetAndStoreTokens` 產生），並在頁尾加一段小型 inline script，把既有的「登出」導覽連結（`.app-nav__link--logout`）改成呼叫 `fetch POST /Account/Logout`（帶 CSRF header）再導頁，取代原本沒有作用的 `href="#"`。 |
| `_AuthLayout.cshtml` | 同樣加入 `<meta name="csrf-token">`，供 Login/Register 頁面的 `auth.js` 讀取。 |

### `wwwroot/js/auth.js`

- 新增 `getCsrfToken()`（讀 meta tag）、`showAlert()`（顯示/隱藏 `#login-alert` / `#register-alert`）、`submitAuthRequest()`（共用的 `fetch` 包裝，自動帶 `Content-Type` 與 CSRF header）。
- Login 表單：驗證通過後改為呼叫 `submitAuthRequest("/Account/Login" + 目前的 query string, ...)`，成功時用回應的 `redirectUrl` 導頁；失敗時把錯誤訊息顯示在 `#login-alert`。查詢字串一併轉發是為了保留 `returnUrl`（例如被 `[Authorize]` 導回登入頁時帶的參數）。
- Register 表單：同樣邏輯，呼叫 `/Account/Register`。
- 之前的版本只做前端驗證、把按鈕文字換成「登入中…」就結束，完全沒有呼叫後端；現在才是真正打 API。

### `appsettings.json`

新增 `Jwt` 區段（`Issuer`/`Audience`/`ExpirationHours` 有預設值；`SigningKey` 留空字串，比照 `ConnectionStrings:DefaultConnection` 的作法，真正的值透過 `dotnet user-secrets` 設定，不進 Git）。

### `Tests/AuthFlow.Tests/`

獨立的 xUnit 測試專案（`ProjectReference` 指回主專案），對應 `AGENTS.md` Testing Priorities 的 Register/Login：

- `Unit/ReturnUrlPolicyTests.cs`：驗證 `IsSafeLocalUrl` 對合法的站內相對路徑放行，對 `null`/空字串/絕對 URL/`//` 開頭/非 `/` 開頭全部擋下（7 個測試）。
- `Integration/AuthServiceTests.cs`：用 EF Core InMemory Provider 建立 `AppDbContext`，涵蓋「註冊成功並可查到使用者」「重複 Email 註冊被拒」「密碼正確可登入」「密碼錯誤被拒」「Email 不存在被拒」共 5 個案例。

## 密鑰管理

`Jwt:SigningKey` 透過 `dotnet user-secrets set "Jwt:SigningKey" "<64-byte random base64>"` 設定在本機 user-secrets store（與既有的 `ConnectionStrings:DefaultConnection` 用同一個 `UserSecretsId`，因此在同一台機器上的任何 worktree 都共用同一份密鑰，不會因為切分支而失效）。正式環境應改用環境變數或雲端密鑰管理服務注入，並且**不可**與開發環境共用同一把金鑰。

## 尚未涵蓋的部分

- **Rate limiting**、**Auth failure 記錄**：已於後續補上，詳見 [`Docs/RateLimitingAndAuthLogging.md`](RateLimitingAndAuthLogging.md)。
- **Token 撤銷 / 單裝置登出**：目前 JWT 是無狀態的，效期內即使使用者登出，若 token 被複製到其他地方仍可使用到過期為止（8 小時）。V1 範圍內沒有實作黑名單或 refresh token 機制，這點與多數單人使用的小型專案取捨一致，但若未來有安全性更高的需求，需另外設計。
- **Email 驗證、忘記密碼、改密碼、OAuth**：依 `AGENTS.md` 定義為 V1 不做的項目，此次也沒有實作。
