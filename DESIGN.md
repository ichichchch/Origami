# Origami DESIGN.md

> 供 AI UI 生成／迭代使用的設計系統規範（[awesome-design-md](https://github.com/VoltAgent/awesome-design-md) / Stitch 格式）。
> 本文件描述**現行已實作**的設計系統；修改 UI 前先讀此文件，改動後同步更新此文件。
> 令牌以 `{token.name}` 標記，實際落地為 [app.css](src/Origami.Web/wwwroot/app.css) 的 CSS 變數 `var(--name)`。

# Overview

Origami 是 AI 原生 CMS：一個產品、兩種性格、一套令牌。

- **公開網站**（`.site-shell`，Static SSR）：編輯式閱讀體驗，**以 Apple 設計語言為參照**（[awesome-design-md/apple](https://github.com/VoltAgent/awesome-design-md/blob/main/design-md/apple/DESIGN.md)）——紙白底（與 Apple parchment #f5f5f7 同族）、單一品牌強調色、膠囊主 CTA、毛玻璃吸頂頁頭、17px 正文與 600 字重收窄字距的大標題。安靜、留白、內容優先——版面是紙，內容是主角。
- **管理後台**（`.admin-shell`，Interactive Server）：墨黑／紙白的中性工作台。恒深色側欄、白膠囊 active 指示、克制的微交互與進場動效。管理介面**不借用站點品牌色**，以黑白建立自己的工具感識別；按鈕維持 7px 矩形（工具面語法，與公開站的膠囊語法刻意區分）。

兩者共用同一組語義令牌（surface／ink／border／語義色）與基礎組件；公開站再疊加「受控版型」層（主題預設與主題包可換字體棧、欄寬、行距、密度、卡片樣式與品牌色），後台則以檔尾覆蓋塊鎖定黑白。

設計原則：內容優先；單色調＋單強調色；以髮絲線與留白分層，不以陰影與漸變裝飾（後台僅低調中性陰影）；全部色彩經令牌派生（`color-mix`），不硬編碼色值；44px 觸控目標、焦點環、reduced-motion／高對比偏好為內建要求而非事後補丁。

## Colors

### 品牌與強調色（公開站）
- **品牌基色**（`{colors.accent-base}` → `var(--accent-base)`）：站點可自訂的十六進位色，預設 `#2f6f4e`（reading 預設的深紙鶴綠；CSS 根回退 `#315d48`）。唯讀可選 `#333333`（minimal）與 `#8a5a2b`（gallery）。
- 派生規則（在 `.site-shell` 上重算，深淺模式各自派生）：
  - 填充 `{colors.accent}`＝基色；hover 混 16% 黑。
  - 前景文字 `{colors.accent-ink}`＝基色；深色模式以 `color-mix(in oklab, base 60%, white)` 提亮至可讀對比。
  - 柔底 `{colors.accent-soft}`＝基色混 12% 白（深色模式改混深底）。
- 用途：主按鈕填充、連結前景、active 導覽、選取 chip、小工具強調、文字選取色（`::selection` 以 `{colors.accent-soft}` 品牌淡染）。**一個站點只有一個強調色**，不做第二主色。

### 後台中性識別（2026-11-04 起）
- **墨黑**（`{colors.admin-accent}`＝`#18181b`）：後台的主操作色，代替品牌色承擔按鈕、連結、焦點；hover 走純黑 `#000`；柔底 `#ececef`。
- **側欄恒深**：`linear-gradient(180deg,#131318,#101014)`（深色模式 `#0b0b0e`），文字令牌在側欄範圍內翻轉為淺色。
- 深色後台：紙 `#101014`、面 `#1a1a1f`、墨 `#eaeaee`、強調翻白 `#fafafa`。

### Surface
| 令牌 | 淺色 | 深色 | 用途 |
|---|---|---|---|
| `{colors.paper}` | `#f7f7f8` | `#101013` | 頁面畫布 |
| `{colors.surface}` | `#ffffff` | `#1a1a1e` | 卡片、面板、輸入底 |
| `{colors.surface-sunken}` | `#f1f1f3` | `#151518` | 表頭、嵌入塊、停用輸入 |
| `{colors.surface-raised}` | `#ffffff` | `#212127` | 對話框等浮層 |
| `{colors.code-bg}` | `#f0f0f3` | `#0d0d10` | 行內代碼與代碼塊 |
| `{colors.border}` | `#e5e5e8` | `#2a2a30` | 髮絲分隔線（1px） |
| `{colors.border-strong}` | `#cfcfd6` | `#45454e` | 輸入框與強邊框 |

### Text
- `{colors.ink}` `#1b1b1f`／深色 `#e8e8ec`：標題與正文。
- `{colors.muted}` `#5d5d66`／深色 `#9c9ca6`：次要文字、日期、表頭、說明。
- 連結＝`{colors.accent-ink}`（後台＝墨黑），hover 加深並保留底線偏移 `.23em`。

### Semantic
- **Danger**：填充 `#a43e35`（hover `#843129`）、文字 `#a43e35`、柔底 `#fff2ef`。破壞性操作與錯誤。
- **Success**：文字 `#49703e`、柔底 `#edf4e8`。已發布狀態與成功通知。
- **Warning**：文字 `#7d6027`、柔底 `#f3efdf`。草稿狀態與提示。
- 深色模式各有對應提亮值；語義色不做裝飾用途。
- **焦點環** `{colors.focus}`：公開站**跟隨品牌色派生**——淺色＝`var(--accent-ink)`，深色＝`color-mix(in oklab, accent-base 60%, white)`；後台鎖定中性（淺色 `#18181b`、深色 `#d4d4dc`），`:root` 回退 `#699d78` 僅供無殼層上下文。

## Typography

### Font Family（白名單，三棧）
- **sans**：`"Segoe UI", "Microsoft JhengHei", "PingFang TC", sans-serif` — 後台全域；公開站 minimal／gallery 預設。
- **serif**：`"Noto Serif TC", "PMingLiU", Georgia, serif` — reading 預設的正文與標題；公開站標題字體由 `var(--font-heading)` 驅動。
- **mono**：`Consolas, monospace` — 代碼、slug、API 憑證輸入。
- 不引入 web font、不新增字體；主題包只能引用此三棧的白名單鍵（sans／serif／mono）。

### Hierarchy
| 階層 | 規格 | 用途 |
|---|---|---|
| 公開頁頭 | `clamp(2.5rem, 5vw, 3.6rem)`／600／襯線／行高 1.15／`-.015em` | 列表頁大標（`public-heading h1`） |
| 文章標題 | `clamp(2rem, 5vw, 3rem) × var(--heading-scale)`／襯線／行高 1.25／`-.015em` | 文章詳情 `article-heading h1` |
| 後台頁標 | `2rem`／700／`-.045em` | `page-heading h1`（800px 以下 1.8rem） |
| h1 基準 | `clamp(1.8rem, 3vw, 2.5rem)`／700／`-.045em`（公開站殼內覆寫為 600／`-.02em`） | 對話框、登入卡等容器內 |
| h2 | `1.1rem`／650／`-.02em` | 面板標題、小節 |
| h3 | `.98rem` | 卡片標題 |
| 正文 | `1.0625rem`（17px，Apple 參照）／行高 `var(--prose-line, 1.95)` | `.prose`（預設決定 1.7–2.0；行動端 1rem） |
| UI 正文 | `.8–.9rem` | 表單說明、表格單元、導覽 |
| 小字 | `.7–.78rem` | 邊註、media 卡中繼資料 |
| 微標籤 | `.66–.74rem`／650／字距 `.07–.14em`／大寫 | `eyebrow`、`nav-label`、`th`、`widget-title` |

### Principles
- 中文長文行高 1.7–2.0，由主題變數驅動；列表密度由 `--list-gap`（1.2–2.2rem）控制。
- **Apple 參照字排**：公開站大標題 600 字重、負字距收在 `-.015em～-.02em`（比拉丁字排更保守，照顧 CJK）；正文 17px 的「閱讀節奏」；後台頁標維持 700／`-.045em` 的更重工具面階梯。
- 日期、計數、指標一律 `font-variant-numeric: tabular-nums`。
- 任何介面文字經 `UiText.T(key, 原文)` 走三語資源，不寫死。

## Layout

### Spacing System
- 基準 8px（`.5rem` 遞增）；面板內距 `1.2–1.7rem`；頁首列間距 `2rem`。
- 公開主區 `padding: 5rem 0 6rem`（行動端 `3rem 0 3.5rem`）；頁頭大標下緣距 3.5rem、文章標題區下緣距 3.5rem——Apple 式段落節奏。

### Grid & Container
- **公開站殼** `.site-shell`：`max-width 1120px`＋左右 `2rem`（≤640px 收 `1.2rem`）。
- **閱讀欄** `.reading-column`：`max-width var(--reading-width, 68ch)` 置中（可調 48–96ch）。
- **雙欄** `.layout-columns`：72rem 以下單欄；以上 `1fr + 17rem` 側欄（sticky top 1.6rem），欄距 2.6rem。無側欄小工具時直接輸出單欄。
- **後台殼** `.admin-shell`：`238px + 1fr`（≤1100px 收 205px；≤800px 側欄轉頂部橫向導覽）。
- **後台主區** `.admin-main`：`max-width 1440px` 置中，`padding 2.8rem clamp(1.25rem,3vw,3rem) 3rem`（≥1500px 頂距 3.5rem）。
- 工作格線：編輯器 `1fr + 278px` 側欄；管理頁 `.65fr + 1.35fr`；儀表板 `1.55fr + .65fr`（均 ≤1100px 收單欄）。

### Whitespace Philosophy
- 公開站以髮絲線與行距分層，文章列表行距 1.2–2.2rem 由主題決定；不追求填滿。
- 後台以面板（panel）為工作單元，面板之間 `1.5rem`；列表頁重密度，詳情頁重呼吸。

## Elevation & Depth

| 層級 | 處理 | 用途 |
|---|---|---|
| 0（平） | 無陰影，1px `{colors.border}` | 公開站全部、後台面板靜態 |
| 1（微浮） | `0 1px 2px #0a0a0c1f` | 按鈕靜態 |
| 2（面板） | `0 1px 2px #0a0a0c0d, 0 10px 28px -20px #0a0a0c59` | 後台 `.panel`；深色加深 |
| 3（懸浮） | hover `-2px` 位移＋擴散陰影 | `metric-card`、按鈕 hover（`-1px`） |
| 4（浮層） | `--shadow-raised: 0 16px 70px rgba(10,10,12,.16)`＋`#00000073` 背板 | 對話框 |

### Motion
- 時長 120–250ms，緩動 `cubic-bezier(.22,1,.36,1)`；僅 transform／opacity／色彩。
- 簽名動效：品牌折紙立方體 18s 自轉（唯一持續動畫）、`admin-enter` 進場階梯（45ms 遞延）、表格行 hover 掃過、active 導覽白膠囊。
- 全域 `prefers-reduced-motion: reduce` 一律靜止（立方體定格為固定角呈現）。

## Shapes

| 圓角 | 值 | 用途 |
|---|---|---|
| `{radius.panel}` | 12px（`--radius`） | 面板、小工具、對話框、通知、登入卡 |
| `{radius.control}` | 7px | **後台**按鈕、輸入框、file 按鈕 |
| `{radius.pill}` | 999px | **公開站**主 CTA、次按鈕（ghost pill）、側欄搜尋框——Apple 參照的膠囊語法，僅限 `.site-shell` 範圍 |
| `{radius.media}` | 8px | 媒體卡、嵌入音視訊（影片 10px） |
| `{radius.chip}` | 4px | chip、count |
| `{radius.badge}` | 5px | 狀態徽章 |
| `{radius.nav}` | 8px | 後台導覽項（active 成膠囊） |
| 圓形 | 50% | 頭像、空狀態符號——僅此兩處 |

**按鈕是雙語法**：公開站（`.site-shell` 內）主／次 CTA 為全圓膠囊（Apple 參照），17px／400 字重、active `scale(.96)`；後台一律 7px 矩形。徽章是圓角矩形＋前導圓點，不是膠囊。受控主題包可在殼層覆寫 `--radius`（如 16px），系統組件自動跟隨；未覆寫時用上表預設（膠囊不受 `--radius` 影響）。

## Components

### Buttons
- **`.button`** 主操作：填充 `{colors.accent}`（後台墨黑）、白字、圓角 7px、`min-height 43px`。hover 上浮 1px＋陰影；disabled `.5` 透明度。
  - **公開站變體**（`.site-shell .button`，Apple 參照）：全圓膠囊 999px、`1.0625rem`／400 字重、`-.01em` 字距、active `scale(.96)`。
- **`.button-secondary`** 次操作：後台＝白底＋`{colors.border-strong}` 描邊、ink 文字；**公開站＝ghost pill**（透明底＋強調色描邊與文字，hover 轉 `{colors.accent-soft}` 柔底）。
- **`.button-danger`** 破壞性操作：danger 填充；僅用於刪除／永久刪除等明確破壞動作。
- **`.button-small`** 36px 緊湊版（表格行內、分頁；公開站同樣膠囊化、`.9rem`）。
- **`.text-button`／`.text-danger`** 行內文字按鈕：無底無框、底線、36px 觸控高。
- 全寬變體 `.full-width` 用於登入主提交。

### Cards & Panels
- **`.panel`** 後台工作面板：surface 底、1px 邊、12px 圓角、層級 2 陰影；`panel-toolbar`（帶下邊框的工具列）、`panel-intro`、`settings-panel` 等內距變體。
- **`.metric-card`** 儀表板指標卡：`1.75rem`/700 等寬數字、hover 上浮。
- **`.widget`** 公開小工具（7 型）：surface 底、12px 圓角、`widget-title` 大寫微標籤；頁尾小工具去底透明。
- **`.media-card`／`.media-choice`** 媒體格點卡：等比縮圖＋中繼資料；hover 邊框轉強調色。
- **`.package-card`** 市場包卡：kind 微標籤＋即時 `theme-preview`。
- **`.auth-card`** 登入卡：460px、2rem 內距、12px 圓角。
- **`.confirm-dialog`** 460px 對話框＋`dialog-backdrop` 半透明背板；確認對話框一律先渲染背板防誤觸。
- **`.theme-preview`** 設定／市場即時預覽：複用 `.site-shell` 變數派生，未儲存調整即時反映。

### Inputs & Forms
- 輸入：`min-height 44px`、7px 圓角、`{colors.border-strong}` 描邊；focus 描邊轉強調色；disabled 降為 sunken 底。
- `.field`（label 600／`.875rem`）＋`.field-help`（muted 說明）為標準表單節奏；checkbox／radio 以 `accent-color` 跟隨強調色。
- `.input-prefix` 前綴輸入（如網址）；`.drop-zone` 虛線拖放區，drag-over 轉強調色。
- 驗證：`.validation-message` danger 文字＋`.invalid` 紅框。

### Notices & Status
- **`.notice`** 通知條：左 3px 邊條＋柔底；變體 `notice-error`／`notice-success`。
- **`.status-badge`** 狀態徽章：前導圓點＋柔底；`.published` 綠、`.draft` 警告色。
- **`.chip`** 分類／標籤chip：accent-soft 底、4px 圓角；選中態加外框。
- **`.count`** 計數小徽章；**`.empty-state`** 空狀態（圓形符號＋標題＋說明）。

### Navigation
- **公開站**：`site-header`＝**Apple 式吸頂毛玻璃欄**（sticky top、64px、`color-mix(paper 82%)`＋`backdrop-filter: blur(16px) saturate(1.6)`、下髮絲線；`prefers-reduced-transparency` 退實色；≤640px 轉靜態縱排）。站名 1.25rem/650/`-.02em`；導覽 muted 無底線、hover 轉強調色＋底線；子頁導覽項縮小縮排；**當前頁以 `aria-current="page"` 標記**（ink 色＋650 字重，路徑解碼後精確比對，外部連結不參與）。
- **後台側欄**：品牌立方體＋`admin-nav`；項目 8px 圓角、hover 微亮、**active＝白膠囊黑字＋陰影**；尾端箭頭 hover 輕推。分組以 `.nav-label` 大寫微標籤。
- **頂欄** `admin-topbar`：sticky＋毛玻璃（blur 12px），網域＋權限提示＋呼吸圓點。

### Tables
- `th`：sunken 底、`.68rem` 大寫、字距 `.07em`；`td` `1.2rem 1.4rem` 內距；行 hover 3.5% ink 掃過；`.table-scroll` 橫向溢出保護。
- 專用單元：`.title-cell`（標題＋slug）、`.date-cell`、`.status-badge`、`.table-actions`、`.select-cell`。

### Markdown 渲染（`.prose`）
- 公開站正文：字體／行距由主題變數驅動；blockquote 左 3px 強調線；code 用 code-bg；圖片 8px 圓角。
- 受控提示塊 `.note`／`.tip`／`.warning`：左 4px 課題色邊＋柔底；`.toc` 目錄卡；`.details-block` 可摺疊；`.media-embed` 原生音視訊卡。

### 系統層組件
- **重新連線對話框**（`#components-reconnect-modal`，native `<dialog>`）：**渲染於兩個殼層內**（MainLayout／AdminLayout 各自包含，同一份文件只有一個 layout 在場）——令牌隨殼層解析，公開站跟品牌色、後台呈黑白；否則會落到 `:root` 預設綠。樣式全部走令牌：`surface-raised` 底＋`--radius`＋`--shadow-raised`、按鈕同 `.button` 規格、連線動畫環用 `{colors.accent}`。`#blazor-error-ui` 錯誤條同理置於 `.admin-shell` 內。
- **儀表板圖表**（Chart.js，`Dashboard.razor.js`）：調色板由 JS 讀取 `.admin-shell` 計算樣式產生（`--accent`／`--muted`／`--border`／`--surface`），並監聽 `data-color-scheme` 變更與系統深色切換即時重繪；載入失敗時保留伺服端渲染的 CSS 條 fallback，不空白。

## Do's and Don'ts

### Do
- 一律引用令牌（`var(--accent-ink)` 等）而非色值；新色調經 `color-mix` 從既有令牌派生。
- 公開站以髮絲線與留白分層；保持單一強調色。
- 後台保持黑白中性：新組件用 ink／paper 語義，不引入站點品牌色與彩色裝飾。
- 保持觸控目標：按鈕 ≥43px（small ≥36px）、輸入 44px。
- 任何可見文字走 `UiText.T`（三語同步）；任何互動元素有焦點可見樣式。
- 動效只用 transform／opacity，尊重 `prefers-reduced-motion`。
- 數字（日期、計數、指標）用 `tabular-nums`。

### Don't
- 不做大圓角內容卡片、漸變背景、陰影堆疊——這不是 Origami 的語言（`--radius` 面板體系除外，主題包覆寫時跟隨）。
- **不在後台使用膠囊按鈕**——膠囊是公開站 CTA 的品牌語法；後台矩形 7px 是工具面語法，兩者不可混用。
- 不繞過令牌硬編碼 hex（主題包與深色模式會失效）。
- 不新增字體或 web font（三白名單棧之外一律拒絕）。
- 不把未清理內容直出 `MarkupString`；Markdown 一律走安全渲染管線。
- 不在後台使用語義色做裝飾（僅表達狀態）。
- 不為視覺新增互動障礙（隱藏元素不承擔資訊、圖片有 alt、對話框有背板）。

## Responsive Behavior

### Breakpoints
| 寬度 | 變化 |
|---|---|
| ≥1500px | 後台主區頂距加大 |
| ≥72rem | 公開站啟用 17rem 側欄雙欄 |
| ≤1100px | 後台側欄 205px；編輯器／管理／儀表板格線收單欄 |
| ≤800px | 後台側欄轉頂部橫向導覽（隱藏計數與分組標籤）；頂欄隱藏；文章列表 time 移至標題上方 |
| ≤640px | 公開殼內距 1.2rem；頁頭縱排 |
| ≤520px | 媒體格 2 列；分頁與徽章縮小；表單頁 1.2rem 內距 |
| ≤480px | 自訂欄位定義列表轉單欄 |

### Touch Targets
- 輸入 44px；主按鈕 43px；small 按鈕／文字按鈕 36px；小工具搜尋 38px。

### Collapsing Strategy
- 格線一律優先保住內容欄（`minmax(0,1fr)`），次要欄先收起或轉橫向。
- 表格靠 `.table-scroll` 橫向滾動，不擠壓欄位；公開長文以 `--reading-width` 約束永不滿寬。
- 頁尾小工具 `auto-fit minmax(13rem,1fr)` 自然折行。

## Accessibility
- `skip-link` 跳至主要內容；`:focus-visible` 3px 焦點環＋4px 偏移；導覽後 `h1[tabindex=-1]` 不顯示焦點環。
- `prefers-contrast: more` 提高次要文字與邊界對比；深色模式全文令牌翻轉，不留淺色死角。
- 對話框背板阻擋誤觸；`aria-label`／`aria-current`／`role` 隨組件落地；列印樣式剝離導覽、側欄與後台框架；正文錨點標題帶 `scroll-margin-top` 供目錄跳轉留白。

## Iteration Guide
1. 一次只改一個組件；引用本文件令牌與類名，不引入新令牌除非先寫進本文件。
2. 樣式改動落在對應檔案：全域與後台 → `app.css`（後台黑白覆蓋塊**必須保持在檔尾**，靠來源順序壓過全域）；佈局殼 → `MainLayout.razor.css`；元件私有 → 同名 `.razor.css`。
3. 深色模式成對提交：任何新色都要給 `[data-color-scheme="dark"]` 對應值（含 admin 變體）。
4. 介面文字走 `UiText.T` 並補三語資源；改動後以瀏覽器走查淺色／深色與 800px 行動寬度。
5. 主題預設相關改動需核對 reading／minimal／gallery 三預設與 pkg 主題包的表現，勿使任何預設退化。

## Known Gaps
- 字體依賴系統棧，未打包 Noto Serif TC；非中文環境觀感可能漂移。
- i18n 為漸進抽取：後台部分頁面（如 ContentList 篩選與批次操作列）仍有少量硬編碼繁中文字，按既有逐頁抽取工作流補齊。
- 後台多數頁面尚未有設計級走查記錄（本文件後台部分以程式碼與 2026-11-04 重設計為據）。
