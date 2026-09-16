# 首次登入頁前端實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 建立可由 /Account/Login 開啟的繁體中文、響應式、可鍵盤操作登入頁前端，不串接帳密驗證或 JWT。

**Architecture:** 認證頁使用專屬 _AuthLayout.cshtml，隔離既有一般頁導覽。AccountController 只回傳登入 View；Login.cshtml 負責語意化標記；auth.css 與 auth.js 各自承擔樣式與純本地互動，沒有 API 或儲存行為。

**Tech Stack:** .NET 10、ASP.NET Core MVC Razor、Bootstrap（既有）、原生 CSS、原生 JavaScript、瀏覽器內建 HTML 驗證。

---

## 檔案結構

| 檔案 | 動作 | 責任 |
| --- | --- | --- |
| Controllers/AccountController.cs | 建立 | 將 GET /Account/Login 導向 Razor View，無商業邏輯。 |
| Views/Shared/_AuthLayout.cshtml | 建立 | 認證頁文件骨架、zh-Hant、必要靜態資源。 |
| Views/Account/Login.cshtml | 建立 | 登入頁的語意表單、品牌、註冊入口與通用錯誤容器。 |
| wwwroot/css/auth.css | 建立 | auth 專用 token、RWD、互動狀態與可存取 focus。 |
| wwwroot/js/auth.js | 建立 | 密碼可見性與提交中按鈕狀態；不得呼叫 API。 |

本計畫不修改 Program.cs、資料模型、服務、安全設定或現有 _Layout.cshtml。

### Task 1: 建立最小 MVC 入口與認證版型

**Files:**
- Create: Controllers/AccountController.cs
- Create: Views/Shared/_AuthLayout.cshtml

- [ ] **Step 1: 建立控制器的失敗檢查**

在瀏覽器開啟 /Account/Login。預期得到 404，因為 AccountController.cs 為空且沒有 Login action。

- [ ] **Step 2: 實作只回傳 View 的 controller**

將 Controllers/AccountController.cs 完整替換為：

~~~csharp
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers;

public sealed class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }
}
~~~

- [ ] **Step 3: 建立不含一般導覽的 Razor layout**

建立 Views/Shared/_AuthLayout.cshtml：

~~~cshtml
<!DOCTYPE html>
<html lang="zh-Hant">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - LTM</title>
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/css/auth.css" asp-append-version="true" />
</head>
<body class="auth-page">
    <main class="auth-page__main">@RenderBody()</main>
    <script src="~/js/auth.js" asp-append-version="true"></script>
</body>
</html>
~~~

- [ ] **Step 4: 建置並確認入口**

Run: dotnet build "Lyubishchev Time Management.csproj"

Expected: Build succeeded. 尚未有 View 時，路由已命中但會回報 View 找不到。

- [ ] **Step 5: Commit**

~~~bash
git add Controllers/AccountController.cs Views/Shared/_AuthLayout.cshtml
git commit -m "feat: add login page MVC shell"
~~~

### Task 2: 建立登入表單與靜態頁內容

**Files:**
- Create: Views/Account/Login.cshtml

- [ ] **Step 1: 確認 View 缺失狀態**

重新開啟 /Account/Login。尚未建立 Views/Account/Login.cshtml 時，預期開發環境會顯示 View 找不到；這證明 controller 已導向正確。

- [ ] **Step 2: 建立完整 Razor View**

建立 Views/Account/Login.cshtml：

~~~cshtml
@{
    Layout = "_AuthLayout";
    ViewData["Title"] = "登入";
}

<section class="login-shell" aria-labelledby="login-heading">
    <div class="login-card">
        <a class="brand" asp-controller="Home" asp-action="Index" aria-label="LTM 首頁">
            <span class="brand__mark">LTM</span><span class="brand__name">TIME LOG</span>
        </a>
        <header class="login-card__header">
            <h1 id="login-heading">歡迎回來</h1>
            <p>登入後，繼續如實記錄你的時間。</p>
        </header>
        <div class="form-alert" id="login-alert" role="alert" hidden></div>
        <form class="login-form" id="login-form" novalidate>
            <div class="form-field">
                <label for="email">電子郵件</label>
                <input id="email" name="email" type="email" autocomplete="email" required aria-describedby="email-error" />
                <p class="field-error" id="email-error" aria-live="polite"></p>
            </div>
            <div class="form-field">
                <label for="password">密碼</label>
                <div class="password-field">
                    <input id="password" name="password" type="password" autocomplete="current-password" required aria-describedby="password-error" />
                    <button class="password-toggle" id="password-toggle" type="button" aria-label="顯示密碼" aria-pressed="false">顯示</button>
                </div>
                <p class="field-error" id="password-error" aria-live="polite"></p>
            </div>
            <button class="login-submit" id="login-submit" type="submit">登入</button>
        </form>
        <p class="register-prompt">還沒有帳號？ <a href="#" aria-label="前往註冊">立即註冊</a></p>
    </div>
    <footer class="auth-footer">© 2026 LTM · 私人時間紀錄</footer>
</section>
~~~

註冊連結暫時使用 #；本任務不建立註冊路由或頁面。

- [ ] **Step 3: 手動驗證初始標記**

Run: dotnet run --project "Lyubishchev Time Management.csproj"

Expected: /Account/Login 回傳 200；頁面有一個 h1、email/password 欄位、登入 submit button 和註冊連結。

- [ ] **Step 4: Commit**

~~~bash
git add Views/Account/Login.cshtml
git commit -m "feat: add login page markup"
~~~

### Task 3: 完成登入頁視覺、RWD 與焦點狀態

**Files:**
- Create: wwwroot/css/auth.css

- [ ] **Step 1: 建立視覺失敗檢查**

在裝置模式開啟 /Account/Login。未有 CSS 時，預期卡片不會置中、按鈕不是珊瑚紅、Tab focus 沒有一致視覺。

- [ ] **Step 2: 建立 auth 專用樣式**

建立 wwwroot/css/auth.css：

~~~css
:root{--ink:#252a34;--muted:#707887;--line:#d9dde5;--surface:#fff;--canvas:#f8f9fb;--accent:#e5533d;--focus:#2f455c}*{box-sizing:border-box}body.auth-page{margin:0;min-width:320px;color:var(--ink);background:var(--canvas);font-family:system-ui,-apple-system,"Segoe UI",sans-serif}.auth-page__main,.login-shell{min-height:100vh}.login-shell{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:24px;padding:24px}.login-card{width:min(100%,400px);padding:32px;border:1px solid #e7e9ef;border-radius:12px;background:var(--surface);box-shadow:0 12px 36px rgb(37 42 52 / 7%)}.brand{display:inline-flex;gap:8px;color:inherit;font-size:.8125rem;font-weight:800;letter-spacing:.04em;text-decoration:none}.brand__name,.login-card__header p,.register-prompt,.auth-footer{color:var(--muted)}.brand__name{font-weight:600}.login-card__header{margin:36px 0 24px}.login-card h1{margin:0;font-size:1.75rem;letter-spacing:-.03em}.login-card__header p{margin:8px 0 0;font-size:.9375rem}.login-form{display:grid;gap:18px}.form-field{display:grid;gap:6px}.form-field label{font-size:.875rem;font-weight:700}.form-field input{width:100%;min-height:44px;padding:10px 12px;border:1px solid var(--line);border-radius:6px;color:var(--ink)}.password-field{position:relative}.password-field input{padding-right:64px}.password-toggle{position:absolute;top:50%;right:8px;padding:5px 8px;border:0;transform:translateY(-50%);background:transparent;color:var(--focus);font-size:.8125rem;font-weight:700;cursor:pointer}.login-submit{min-height:44px;border:0;border-radius:6px;background:var(--accent);color:#fff;font-weight:800;cursor:pointer}.login-submit:hover{background:#ce412d}.login-submit:disabled{cursor:wait;opacity:.7}.form-field input:focus,.password-toggle:focus-visible,.login-submit:focus-visible,.brand:focus-visible,.register-prompt a:focus-visible{outline:3px solid rgb(47 69 92 / 30%);outline-offset:2px;border-color:var(--focus)}.field-error{min-height:1.2em;margin:0;color:#b42318;font-size:.8125rem}.form-alert{margin-bottom:16px;padding:12px;border-radius:6px;color:#8a1c12;background:#fff0ee;font-size:.875rem}.register-prompt{margin:22px 0 0;text-align:center;font-size:.875rem}.register-prompt a{color:#d34430;font-weight:800}.auth-footer{font-size:.75rem;text-align:center}@media(max-width:479px){.login-shell{justify-content:flex-start;padding:24px 20px}.login-card{padding:28px 20px}.login-card__header{margin-top:30px}}
~~~

- [ ] **Step 3: 驗證 RWD 與鍵盤**

在 320px、768px、1440px 檢查卡片無水平溢位，手機至少 20px 邊距，桌機置中。Tab 必須依序到 email、password、顯示密碼、登入、註冊，且每個元素有可見 focus。

- [ ] **Step 4: Commit**

~~~bash
git add wwwroot/css/auth.css
git commit -m "feat: style responsive login page"
~~~

### Task 4: 實作純前端互動與最終驗證

**Files:**
- Create: wwwroot/js/auth.js

- [ ] **Step 1: 建立互動失敗檢查**

尚未建立 JavaScript 時，點擊「顯示」不會變更密碼欄型別；空白提交不會在欄位下顯示繁中訊息。

- [ ] **Step 2: 建立零 API 的本地互動**

建立 wwwroot/js/auth.js：

~~~javascript
(() => {
  const form = document.querySelector('#login-form');
  const password = document.querySelector('#password');
  const toggle = document.querySelector('#password-toggle');
  const submit = document.querySelector('#login-submit');
  const fields = [
    { input: document.querySelector('#email'), error: document.querySelector('#email-error'), message: '請輸入有效的電子郵件。' },
    { input: password, error: document.querySelector('#password-error'), message: '請輸入密碼。' }
  ];
  if (!form || !password || !toggle || !submit || fields.some(({ input, error }) => !input || !error)) return;
  toggle.addEventListener('click', () => {
    const shown = password.type === 'text';
    password.type = shown ? 'password' : 'text';
    toggle.textContent = shown ? '顯示' : '隱藏';
    toggle.setAttribute('aria-label', shown ? '顯示密碼' : '隱藏密碼');
    toggle.setAttribute('aria-pressed', String(!shown));
  });
  form.addEventListener('submit', event => {
    event.preventDefault();
    let valid = true;
    fields.forEach(({ input, error, message }) => {
      const invalid = !input.validity.valid;
      error.textContent = invalid ? message : '';
      input.setAttribute('aria-invalid', String(invalid));
      valid &&= !invalid;
    });
    if (!valid) {
      fields.find(({ input }) => !input.validity.valid).input.focus();
      return;
    }
    submit.disabled = true;
    submit.textContent = '登入中…';
  });
})();
~~~

- [ ] **Step 3: 手動驗證互動與零 API 範圍**

1. 連續點擊顯示／隱藏兩次：密碼內容不變，型別在 password 與 text 間切換。
2. 空白提交：email、password 下方顯示指定繁中訊息，焦點移至 email。
3. 輸入 name@example.com 及任意非空密碼後提交：按鈕顯示「登入中…」且 disabled。
4. 瀏覽器 Network 面板沒有 Fetch/XHR 請求。

- [ ] **Step 4: 最終建置與 Commit**

Run: dotnet build "Lyubishchev Time Management.csproj"

Expected: Build succeeded.

~~~bash
git add wwwroot/js/auth.js
git commit -m "feat: add login form interactions"
~~~

## 計畫自我檢查

- 規格的獨立 layout、單欄登入卡、繁中內容、珊瑚紅單一 CTA、註冊文字入口、320px、鍵盤 focus、密碼切換、欄位錯誤、提交中狀態與零 API 範圍，都有對應任務與驗證。
- 計畫沒有未決占位項目、未定義方法或含糊的驗證步驟。
- JavaScript 的每個 selector 都在 Login.cshtml 定義；CSS class 只由 layout 或 View 使用。
