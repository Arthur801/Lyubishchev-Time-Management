# RWD（響應式切版）

依照 [`Docs/superpowers/specs/2026-09-17-rwd-design.md`](superpowers/specs/2026-09-17-rwd-design.md) 與 [`Docs/superpowers/plans/2026-09-17-rwd.md`](superpowers/plans/2026-09-17-rwd.md) 完整實作，對照 `AGENTS.md` 的第 14 項。設計文件本身沒有 TBD。

## 設計思路

這份工作不是新功能，是把既有六個受保護頁面（Dashboard、TimeEntry、Report、Category、Tag、Settings）原本各自複製貼上的側欄/行動 header/底部導覽標記收斂成一份共用局部檢視，同時把「Category、Tag、Settings 在手機上完全沒有底部導覽、是死路」這個既有缺口補起來，並統一手機/平板兩種版面帶在 `dashboard.css` 裡的 shell 層規則（觸控目標尺寸、安全區留白、對話框最大高度）。不改資料模型、API、時區/聚合邏輯、Calendar 的互動模型或 CSV 契約。

三個版面帶完全沿用專案既有、已經在 `dashboard.css` 出現過的斷點（900px 側欄收合、720px 內容堆疊、430px 密度微調），沒有新增第四種導覽模式：

| 版面帶 | 寬度 | 導覽 |
| --- | --- | --- |
| 桌面 | `>=901px` | 238px 固定側欄，無行動 header/底部列 |
| 平板/窄桌面 | `721–900px` | 側欄隱藏，行動 header + 五項底部列 |
| 手機 | `<=720px` | 同上，430px 以下再做密度微調 |

行動裝置底部導覽固定五項：儀表板、歷史紀錄、報表、設定、**更多**。「更多」用原生 `<dialog>` 開一個底部彈出選單，裡面放 Category、Tag、登出——這三個目的地不需要各自佔一個底部列項目，也不需要引入任何選單/對話框第三方套件（`dialog` 原生就提供 focus containment 與 Escape 關閉）。

## 整體流程

1. 使用者在 `<=900px` 寬度看到行動 header（品牌連結）+ 底部五項導覽（`_MobileNavigation.cshtml` 局部檢視渲染，依 `@model string` 決定哪一項有 `mobile-nav__link--active`/`aria-current="page"`）。
2. 點「更多」→ `mobile-navigation.js`（`_Layout.cshtml` 載入的啟動模組）選出 `[data-more-trigger]`/`[data-more-sheet]`/`[data-more-close]` 三個元素，呼叫 `mobile-navigation.mjs` 的 `createMoreSheetController` 接上事件：開啟呼叫 `dialog.showModal()`、同步 `aria-expanded`、把 focus 移到關閉鈕；點 backdrop（`event.target === dialog`）、按關閉鈕、或瀏覽器原生 Escape 都會觸發 `dialog.close()`，而 `dialog` 的 `close` 事件統一負責同步 `aria-expanded` 回 `false` 並把 focus 還給觸發按鈕——三種關閉路徑共用同一段收尾邏輯，不必各自重複。
3. `>=901px` 時側欄照舊顯示，`.mobile-nav`/`.more-sheet` 整段透過 `dashboard.css` 的 `display:none`（沿用既有 `.mobile-header,.mobile-nav{display:none}` 規則）隱藏，More 觸發鈕跟著消失，但因為它只是視覺隱藏、Category/Tag 桌面版本來就有直接側欄連結，不影響任何功能。

## 檔案清單與內容

- `Views/Shared/_MobileNavigation.cshtml`（新增）：`@model string`，四個直接連結（Dashboard/TimeEntry/Report/Settings）+ 一個 `<button data-more-trigger>`（`aria-haspopup="dialog"`、`aria-controls="more-sheet"`、`aria-expanded="false"`，Model 為 `category`/`tag` 時视为 active）。`aria-current="@(...)"` 這種「整個屬性值就是一個 Razor 運算式」的寫法會在運算式是 `null` 時讓 Razor 直接省略該屬性，不會印出 `aria-current=""`，沿用既有頁面「只在真正 active 的連結上寫 `aria-current="page"`」的手寫慣例。下方緊接著一個 `<dialog id="more-sheet" data-more-sheet>`，面板含標題、`[data-more-close]` 關閉鈕、Category/Tag 連結、以及沿用既有 `.app-nav__link--logout` class 的登出連結（讓 `_Layout.cshtml` 既有的委派式登出 handler 繼續攔截點擊）。
- `wwwroot/js/mobile-navigation.mjs`（新增）：`isMoreSection(section)`（`category`/`tag` 回 `true`）、`createMoreSheetController({ trigger, dialog, close })` 純函式工廠，不碰 `document`，可在 Node 環境下用假元件測試。
- `wwwroot/js/mobile-navigation.js`（新增）：`_Layout.cshtml` 載入的啟動模組，選取三個 `data-*` 屬性元素，都存在才建立 controller（讓沒有更多選單的頁面——目前沒有這種頁面，但保留這個防呆——不會丟例外）。
- `Views/Shared/_Layout.cshtml`（修改）：在既有委派式登出 `<script>` 前多插一行 `<script type="module" src="~/js/mobile-navigation.js">`。**順手把這個檔案從既有的 CRLF 換行統一成 LF**——它是全專案唯一一個 CRLF 檔案（其餘所有 `.cshtml`/`.css`/`.js` 都是 LF），加一行新內容後 `git diff --check` 會因為 git 預設的 `core.whitespace` 沒有打開 `cr-at-eol` 而把每一行結尾的 `\r` 誤判成「trailing whitespace」；統一成 LF 之後這個誤判就消失了，且跟全專案慣例一致，不是新問題。
- `Views/Dashboard/Index.cshtml`、`Views/TimeEntry/Index.cshtml`、`Views/Report/Index.cshtml`（修改）：移除各自內嵌的 `<nav class="mobile-nav">` 標記，改成 `<partial name="_MobileNavigation" model="@("dashboard"|"time-entry"|"report")" />`；順手把 Dashboard 側欄/行動 header 的品牌連結從 `href="#"` 改成 `asp-controller="Dashboard" asp-action="Index"`（規格明講「authenticated pages 的品牌不可以用 `href="#"`」），以及 Dashboard 側欄原本「儀表板」項目用 `href="#dashboard-content"`（頁內錨點跳轉）改成跟其他頁面一致的真正自我連結。
- `Views/Category/Index.cshtml`、`Views/Tag/Index.cshtml`、`Views/Settings/Index.cshtml`（修改）：新增 `<partial name="_MobileNavigation" model="@("category"|"tag"|"settings")" />`——這三頁先前完全沒有底部導覽，手機上除了瀏覽器上一頁沒有其他離開方式，現在補齊。
- 六個頁面（修改）：行動 header 移除原本沒有任何作用的 `<button class="icon-button" ...>⚙</button>`（規格明講「mobile header 只是視覺情境，右側不該留 inert 的圖示按鈕」），只留品牌連結。
- `wwwroot/css/dashboard.css`（修改，是這次切版的 shell 權威樣式來源）：
  - `.primary-button` 從 `category-tag.css` 移過來跟 `.outline-button` 放在一起——**這是順手修好的既有 bug**：Settings 頁面的 `Views/Settings/Index.cshtml` 只載入 `dashboard.css`/`settings.css`，從來沒有載入 `category-tag.css`，代表 `#timezone-save` 這個 `class="primary-button"` 的儲存按鈕過去一直是瀏覽器預設樣式、完全沒套用到設計。移到 `dashboard.css`（每頁都載入）後才真正修好。
  - 移除死掉的 `.icon-button` 規則（markup 已經全部拿掉，留著會是沒人用的樣式）。
  - `@media(max-width:900px)`：`.dashboard-content` 底部 padding 從固定 `92px` 改成 `calc(76px + env(safe-area-inset-bottom))`（隨安全區浮動，不會被底部列蓋住）；`.mobile-nav` 加 `min-height:68px`；`.mobile-nav__link` 加 `min-height:44px`（原本只有 `min-width:52px`）；新增 `.mobile-nav__more`（button 版的 `.mobile-nav__link`，重設邊框/背景）；新增整組 `.more-sheet`/`.more-sheet::backdrop`/`.more-sheet__panel`/`.more-sheet__header`/`.more-sheet__close`/`.more-sheet__links`（底部對齊、45% 深色遮罩、`width:min(100%,560px)`、`max-height:min(76vh,620px)`、面板可捲動並含 `env(safe-area-inset-bottom)` padding、關閉鈕與選單連結都是 44px 觸控目標）。
  - `@media(max-width:720px)`：`.primary-button,.outline-button,.timer-toggle,.range-button,.add-tag button` 統一 `min-height:44px`；`.range-presets{flex-wrap:wrap}`；`.entry-modal{max-height:calc(100dvh - 24px)}`+`.entry-modal__panel{max-height:inherit;overflow-y:auto}`（這組選擇器同時涵蓋 TimeEntry 的新增/編輯對話框跟 Category/Tag 的資源對話框，因為兩者共用同一組 class）。
  - 新增獨立的 `@media(prefers-reduced-motion:reduce)` 規則，把 `*` 的 `transition-duration`/`animation-duration` 壓到 `.001ms`、`scroll-behavior:auto`，只移除過場動畫不移除狀態變化本身（focus-visible outline 不受影響）。
- `wwwroot/css/history.css`（修改）：`.entry-name` 加 `overflow-wrap:anywhere`（長名稱/CJK 不會撐開版面）；`<=720px` 加 `.filter-field{width:100%}`（篩選器手機滿版）、`.icon-btn{width:44px;height:44px}`（列表列的編輯/刪除鈕，原本 32px）。
- `wwwroot/css/calendar.css`（修改）：`.calendar-grid` 加 `-webkit-overflow-scrolling:touch`+`scrollbar-width:thin`（週格橫向捲動時有可視的捲軸提示）；`.history-view-toggle` 加 `flex-wrap:wrap`；`<=768px`（跟 `calendar.js` 判斷週/日抓取模式的 `matchMedia('(min-width: 768px)')` 同一個門檻，**刻意不改**，因為改了會讓視覺切換點跟資料抓取切換點不一致）加 `.calendar-nav{width:100%;justify-content:space-between}`。行事曆區塊本來就有 `overflow:hidden`+`text-overflow:ellipsis` 加上 `calendar.js` 既有的 `title`/`aria-label` 屬性提供完整名稱，不需要額外處理。
- `wwwroot/css/report.css`（修改）：`#report-page .chart-legend li{flex-wrap:wrap}`、`.legend-name{overflow-wrap:anywhere}`、`.tag-row{grid-template-columns:minmax(64px,82px) 1fr auto}`（原本固定 `82px`，改成有彈性下限，避免中間的長條被壓到 0 寬）——都用 `#report-page` 前綴，避免影響 Dashboard 也在用的同一批 `.chart-legend`/`.tag-row` 元件（Dashboard 沒有要求要顯示百分比/換行規則跟 Report 不完全一樣）。
- `wwwroot/css/category-tag.css`（修改）：`.resource-name` 加 `overflow-wrap:anywhere`+`min-width:0`（配合既有的 `<=720px` flex-wrap 讓動作按鈕換到獨立一行）。
- `wwwroot/css/auth.css`（修改）：`<=479px` 的 `.auth-page__main`/`.login-card` padding 從 `20px`/`28px 24px` 改成統一 `16px`；新增 `.password-field input{padding-right:84px}`（原本 `70px`，密碼顯示按鈕在小螢幕變 44px 寬後需要更多留白）與 `.password-toggle{min-width:44px;min-height:44px}`。`.login-submit`/`.form-group input` 本來就已經是 `min-height:44px`，沒有重複加規則。
- `Tests/Unit/mobile-navigation.test.mjs`（新增）：5 個測試，`FakeElement`/`FakeDialog` 假元件（`addEventListener`/`dispatch`/`setAttribute`/`getAttribute`/`focus`，`FakeDialog` 額外有 `open`/`showModal`/`close`，`close()` 會像真的 `<dialog>` 一樣觸發 `close` 事件），涵蓋 `isMoreSection` 分類、開啟時 `aria-expanded`/focus 同步、backdrop 點擊關閉＋焦點還原、面板內點擊不關閉、關閉鈕點擊關閉。

## 尚未涵蓋的部分

- **沒有在真的瀏覽器裡做視覺驗證**：這個 session 沒有可用的瀏覽器自動化工具（跟 Dashboard/Calendar/Report 那幾次一樣的既有限制），設計文件列的四組驗收矩陣（320×568、375×667 安全區模擬、768×1024、1024×768，含鍵盤操作、200% 縮放、CJK 長名稱資料集）**沒有**實際跑過，只用 curl 做了結構面驗證：六個頁面各自 `GET` 200、恰好一個 `#more-sheet`、五個底部導覽控制項、active 狀態正確對應目前路由、品牌連結不再是 `href="#"`、`.icon-button` markup 完全移除、`mobile-navigation.js`/`report.css`/`history.css`/`calendar.css`/`category-tag.css`/`settings.css`/`auth.css` 皆可靜態存取。接手後建議優先補視覺驗證這一步。
- Calendar View（第 8 項）與 Report（第 11 項）先前留下的「沒有在真瀏覽器驗證過」缺口，這次也還沒補——RWD 這次只確保它們在新的共用導覽/shell 規則下結構正確，沒有重新做一次完整的手動視覺驗證。
- `wwwroot/css/responsive.css` 依然是空檔案且未被任何頁面載入——設計文件允許「集中在 `dashboard.css`（現況）或一個明確命名的 `responsive.css`」兩種做法，這次選擇前者（`dashboard.css` 本來就已經是 shell 樣式的權威來源），所以 `responsive.css` 維持空檔案是刻意的，不是遺漏。
- 沒有新增任何 CSS 框架、選單/對話框第三方套件、動畫系統、桌面新設計或主題切換，也沒有把「更多」變成一個獨立路由——完全比照設計文件「不在範圍」清單。
- 沒有動到 Calendar 的 overlap 查詢、帳號時區換算、唯讀行為、事件定位計算，也沒有動到 Report 的聚合邏輯或 CSV 契約。
