# Origami — 開發與驗收說明

本文件說明 MVP 的執行方式、專案結構、資料模型、登入授權、前後台頁面、開發順序與測試驗收。
產品範圍與安全基線以 `Agent.MD` 為準；本文件只描述「如何執行與如何驗證」。

**語言 / Languages：** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md)

## 0. 功能總覽

| 領域 | 說明 |
| --- | --- |
| 管理員 | 唯一安全初始化的管理員、Cookie 登入登出、伺服器端密碼復原、TOTP 兩步驟驗證 |
| 文章 | 列表、建立、編輯、草稿、發布、撤回、置頂、排程發布、摘要、確認後刪除與回收桶 |
| 獨立頁面 | 獨立網址、層級式導覽（上層頁面）、循環防護、可作為固定首頁 |
| 分類與標籤 | 平面結構、多對多指派、描述文字、刪除只移除關聯 |
| 媒體 | 圖片上傳、媒體庫、Markdown 插入、alt 文字、引用保護刪除、響應式縮圖與 srcset |
| 公開網站 | 文章列表與分頁、文章與頁面詳情、相鄰與相關文章、按月歸檔、混合搜尋、基本導覽、行動版型 |
| 開放性 | RSS 2.0 訂閱、sitemap.xml、robots.txt、文章級 Open Graph 與 canonical meta、舊網址 301 轉址、JSON 資料匯出與匯入、`/health` 資料庫健康檢查 |
| 編輯安全網 | 既有草稿每 60 秒自動儲存、修訂歷史（每篇 20 筆）、30 天回收桶與還原、草稿分享預覽連結 |
| 可見性 | 私密內容僅管理員可讀、密碼保護內容需解鎖後才顯示，兩者都不進入搜尋與 AI 檢索 |
| 站點設定 | 網站名稱與說明、每頁文章數、訂閱條數、固定首頁、網站圖示、JSON 資料匯出與匯入 |

內容以 **Markdown** 書寫：原始 HTML 被停用，每個輸出網址都經過白名單檢查，儲存的內容無法引入腳本執行。

## 1. 環境需求

| 項目 | 版本 | 說明 |
| --- | --- | --- |
| .NET SDK | 10.0.402 | `global.json` 已鎖定；`dotnet --version` 可確認 |
| PostgreSQL | 15 以上 | 開發、測試與正式環境皆使用 PostgreSQL |
| 瀏覽器 | Edge 或 Chrome | 僅供端到端測試使用 |

套件版本已固定於 `src/Origami.Web/Origami.Web.csproj`：

- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13
- `Markdig` 1.4.2、`HtmlSanitizer` 9.2.1039、`SkiaSharp` 4.153.1

## 2. 必要設定

連線密碼不得寫入原始碼，透過外部設定提供：

```bash
export ConnectionStrings__Cms="Host=127.0.0.1;Port=5432;Database=origami;Username=origami;Password=<密碼>"
export Storage__RootPath="D:/origami-storage"   # 可省略，預設為使用者資料目錄下的 Origami/storage
```

`Storage__RootPath` 必須是絕對路徑，且不得位於應用發布目錄內；未設定時使用預設位置。
資料保護金鑰以 Windows DPAPI 加密存放在 `Storage__RootPath/keys`。

## 3. 執行步驟

```bash
# 1. 套用資料庫遷移（結構由 EF Core migrations 管理，不會刪庫重建）
dotnet run --project src/Origami.Web -- migrate

# 2. 建立唯一管理員（密碼由互動輸入，不接受命令列參數或重導向輸入）
dotnet run --project src/Origami.Web -- admin-init

# 3. 啟動網站
dotnet run --project src/Origami.Web
```

`admin-reset` 可在忘記密碼時由伺服器端重設；重設會輪替安全戳記，使既有登入立即失效。
公開網站入口為 `/`，後台為 `/admin/posts`。

## 4. 專案結構

```text
src/Origami.Web/
  Components/
    Pages/          # 公開網站（Static SSR）：首頁、文章與頁面詳情、404、錯誤頁
    Admin/          # 管理後台（Interactive Server）：內容、分類標籤、媒體、設定
    Account/        # 登入、登出、身份重新驗證
    Layout/         # 公開版型與後台版型、斷線重連提示
  Data/             # DbContext、Identity 註冊、migrations
  Models/           # Content、Category、Tag、MediaAsset、SiteSettings、AdminUser
  Services/         # 內容、分類標籤、媒體、Markdown、站點設定、授權、管理命令
  wwwroot/          # 應用內建 CSS；持久化上傳不存於此
tests/Origami.Tests/
```

## 5. 資料模型

| 模型 | 職責 | 資料庫約束 |
| --- | --- | --- |
| `AdminUser`（Identity） | 帳號、密碼雜湊、安全戳記 | `SingletonKey` 唯一且檢查值為 1，確保只有一位管理員 |
| `Content` | 文章與頁面（`Kind` 區分）、標題、slug、Markdown、摘要、狀態、時間、並發版本 | slug 全站唯一；狀態、標題、slug、發布時間皆有檢查約束；`Version` 為並發權杖；私密與密碼保護不可同時啟用；頁面不可指向自身 |
| `Category` / `Tag` | 文章分類與標籤，含描述文字 | 名稱與 slug 各自唯一於同類 |
| `ContentCategories` / `ContentTags` | 內容與分類標籤的多對多關聯 | 由內容刪除時自動清理 |
| `MediaAsset` | 圖片識別、媒體類型、大小、尺寸、alt 文字、上傳時間 | 檔名唯一；圖片本體存於檔案系統 |
| `SiteSettings` | 單站點名稱與說明、每頁文章數、訂閱條數、固定首頁、網站圖示 | 僅允許 `Id = 1`；首頁與圖示隨內容刪除自動失效 |
| `ContentSlugRedirect` | 舊 slug 到現有內容的永久轉址記錄 | 舊 slug 唯一；隨內容刪除級聯清理 |
| `ContentRevision` | 每次儲存前的前一版快照（標題／本文／摘要） | 隨內容級聯刪除；每篇保留最新 20 筆 |
| `ContentView` | 每篇內容的每日瀏覽計數 | 複合主鍵（內容＋日期）；隨內容級聯刪除 |

內容另有「上層頁面」`ParentId`（僅獨立頁面）、草稿分享預覽權杖、排程時間 `PublishAt` 與回收桶時間 `DeletedAt`；
密碼保護內容另外保存 Identity 密碼雜湊，私密內容對外等同不存在。

狀態有 `Draft`、`Published` 與 `Trashed`；撤回即改回 `Draft`，刪除先進回收桶保留 30 天（可還原、逾期自動清理）。首次發布時間寫入 `PublishedAt` 後不再變更（排程發布、撤回後再發布都一樣）；排程發布以草稿＋未來時間表示，到期由背景工作晉升。

## 6. 登入與授權

- ASP.NET Core Identity + Cookie；Cookie 為 `__Host-` 前綴、Secure、HttpOnly、SameSite=Lax。
- 沒有公開註冊或初始化端點；初始化與密碼恢復只能透過伺服器端命令執行。
- 每個受保護的服務操作都會以新的 `DbContext` 重新確認管理員身分與安全戳記，
  不信任頁面送上的任何身分或角色。登出或重設密碼會輪替安全戳記，既有分頁立即失效。
- 登入失敗 5 次鎖定 15 分鐘；連續失敗超過 20 次時，該「帳號＋來源」組合會收到 429，
  成功登入後即重設。登入與登出皆為正常 HTTP POST，並驗證防偽權杖。
- 兩步驟驗證的第二階段同樣受帳號鎖定約束：帳號被鎖定時即使動態碼正確也無法登入；
  待驗證 Cookie 綁定安全戳記，重設密碼或變更兩步驟驗證後立即失效。
- 私密內容僅管理員可讀，其他訪客得到 404，且不進入關鍵字搜尋、語意檢索與 MCP 工具；
  密碼保護內容需在頁面輸入密碼解鎖，本文與摘要不進入索引與搜尋。
- 內容類型由伺服器決定：建立時明確指定，更新時以資料庫既有類型為準，送上不同類型會被拒絕。
- 獨立頁面的上層關係由伺服器複查並鎖定頁面列，跨分頁並行儲存不會形成循環層級。

## 7. 前後台頁面

公開網站（Static SSR，依請求讀取資料輸出 HTML）：

| 路由 | 內容 |
| --- | --- |
| `/` | 已發布文章列表，依發布時間倒序，含分頁；站點指定固定首頁時改顯示該頁面，帶有分類／標籤篩選時顯示篩選後的列表 |
| `/posts` | 文章列表頁（未指定固定首頁時會轉址到 `/`） |
| `/posts/{slug}` | 文章詳情；未發布或不存在回傳 404；含相鄰文章與語意相關文章 |
| `/pages/{slug}` | 獨立頁面詳情；未發布回傳 404；舊網址在 slug 變更後 301 轉址到新網址 |
| `/archives`、`/archives/{年}/{月}` | 按月歸檔的公開文章一覽 |
| `/search` | 混合搜尋：關鍵字比對結合 pgvector 語意結果，模型未設定時退回關鍵字 |
| `/feed`、`/sitemap.xml`、`/robots.txt`、`/health`、`/favicon.ico` | RSS 2.0 訂閱、網站地圖、爬蟲規則、資料庫健康檢查與網站圖示；僅反映已發布狀態 |
| `/preview/{token}` | 草稿分享預覽（noindex；撤銷或發布後失效，已發布則轉址公開網址） |
| `/account/login` | 管理員登入（Static SSR，Cookie 登入） |

管理後台（Interactive Server，逐頁配置渲染模式）：

| 路由 | 內容 |
| --- | --- |
| `/admin/posts`、`/admin/pages` | 內容列表（含關鍵字搜尋、狀態篩選、回收桶）、發布、撤回、確認後刪除 |
| `/admin/content/{id}` | Markdown 編輯器、預覽、分類標籤指派、媒體插入、排程發布、修訂歷史、密碼保護與私密設定 |
| `/admin/taxonomies` | 分類與標籤的建立、修改、刪除 |
| `/admin/media` | 圖片上傳、媒體庫關鍵字搜尋、alt 文字編輯、引用保護刪除 |
| `/admin/settings` | 網站名稱與說明、每頁文章數、訂閱條數、固定首頁、網站圖示、JSON 資料匯出與匯入 |
| `/admin/security` | 管理員兩步驟驗證（TOTP）啟用與停用 |

固定版型在行動裝置可用；操作期間停用重複提交；未儲存變更離開時會提示，既有草稿每 60 秒自動儲存；
連線中斷時明確告知「未儲存的內容尚未保存」，不會假稱已儲存。

## 8. 開發順序

1. **M1 基礎與登入**：SDK/PostgreSQL 確認、Blazor Web App、Identity、遷移、管理員初始化與恢復。
2. **M2 內容管理**：文章與頁面生命週期、分類標籤、slug 唯一、發布與撤回、樂觀並發。
3. **M3 網站可用**：圖片上傳與驗證、前台 SSR、導覽與分頁、站點設定。
4. **M4 MVP 驗收**：回歸、安全、PostgreSQL 整合、瀏覽器端到端、重啟與備份還原。

## 9. 測試與驗收

```bash
# 完整回歸（需本機有 PostgreSQL 伺服器工具，測試會自行建立隔離叢集）
dotnet test tests/Origami.Tests/Origami.Tests.csproj -c Release
```

測試一律在測試自行建立的隔離 PostgreSQL 叢集上執行（SCRAM 驗證、隨機連接埠），
不會連線到既有資料庫；缺少伺服器工具時會明確標記為未執行，不改用其他 provider 冒充通過。

| 測試檔 | 涵蓋範圍 |
| --- | --- |
| `ContentServiceTests` | 發布閉環、slug 唯一與穩定性、首次發布時間、分頁邊界、關聯去重與刪除、交易回滾、樂觀並發、內容類型由伺服器決定、重複提交被唯一約束拒絕 |
| `TaxonomyServiceTests` | 分類標籤唯一性、分頁、刪除僅移除關聯、樂觀並發 |
| `SettingsServiceTests` | 站點設定儲存、輸出編碼、長度上限、並發更新 |
| `AdminProvisioningTests` | 唯一管理員、重複與並發初始化被拒絕、密碼重設與舊登入失效、取消操作不改變資料 |
| `ProtectedServiceTests` | 未登入或身分失效時，所有受保護服務都拒絕操作 |
| `DatabaseIntegrationTests` | 遷移、唯一約束、交易、UTC 時間保存、資料庫層級規則 |
| `MediaServiceTests` | 圖片上傳、格式偽裝、SVG 拒絕、大小與尺寸上限、路徑穿越、失敗清理、持久化 |
| `MarkdownSecurityTests` | 原始 HTML 停用、危險連結與圖片網址協定、XSS 防護、並行渲染 |
| `HttpSecurityTests` | Cookie 屬性、防偽、登入失敗鎖定與速率限制、資安標頭、公開路由 404、撤回與刪除後不可讀取、未登入無法進入任何後台頁面 |
| `BrowserFlowTests` | 完整發布閉環、雙分頁並發衝突、未儲存提示、斷線恢復、行動版面 |
| `PersistenceTests` | 應用重啟後資料與圖片可用、資料庫與檔案備份還原、原登入仍可用 |
| `CommandLineTests` | 遷移命令套用結構且可重複執行、管理命令拒絕憑證參數與重導向輸入、缺少設定時拒絕啟動 |
| `StoragePathsTests` | 持久化目錄不得位於發布目錄內、相對路徑拒絕、預設位置在應用目錄之外 |
| `PublicSyndicationTests` | 手寫摘要優先與上限、robots/sitemap/feed 僅含已發布內容且撤回後立即下架、詳情頁 og/canonical、feed 條數上限 |
| `PublishingParityTests` | 置頂排序與頁面停用置頂、舊 slug 301 轉址與撤回後停止、圖片 lazy loading、媒體 alt 文字、健康檢查、JSON 匯出與權限 |
| `ContentLifecycleTests` | 回收桶下架與還原、逾期清理、修訂版本數量上限與還原、排程晉升與撤回、預覽連結生命週期 |
| `MediaClosureTests` | 上傳縮圖變體與 srcset、引用保護刪除、變體回填 |
| `HybridSearchTests` | 關鍵字命中（CJK 與大小寫）、語意結果融合去重、撤回後不再命中、前台頁呈現 |
| `SecurityAndProtectionTests` | RFC 6238 向量與 TOTP 流程、admin-reset 停用兩步驗證、密碼保護閘門與搜尋/MCP 排除、每日瀏覽計數 |
| `ProtectionRegressionTests` | 畸形解鎖 Cookie 不外洩摘要、Static SSR 解鎖表單、帳號鎖定時第二階段無法登入、私密與受保護內容不進入嵌入供應商 |
| `ImportExportRegressionTests` | 匯入拒絕越界媒體檔名、匯入還原網站圖示、縮圖變體納入引用保護 |
| `LifecycleRegressionTests` | 回收桶清理不刪除已還原內容、並行儲存不形成頁面循環、排程再發布保留首次發布時間、固定首頁仍可篩選文章列表 |
| `EditorRegressionTests` | 產生預覽連結不掩蓋其他分頁的變更、排程時間視為未儲存變更 |
| `ScheduleTimeTests` | 排程時間按瀏覽器時區換算並跨日光節約時間保持牆上時間、不存在的當地時間被拒絕 |

### 已知限制

- 單站點、單管理員、單一應用實例；不支援多使用者與角色權限。
- 所有已上傳圖片皆為公開資源，草稿中的圖片也不例外；撤回或刪除內容不會隱藏圖片網址。
- 資料保護金鑰以 Windows DPAPI 加密，只能由同一台電腦上的同一個使用者解密。
- 資料匯出檔包含受密碼保護內容的密碼雜湊，可離線嘗試破解，請視為機密檔案並不外流；匯入會以單一交易取代站內全部文字資料，圖片檔需另外還原。
- 不含評論與外掛機制；多使用者、多站點與主題系統不在範圍。
