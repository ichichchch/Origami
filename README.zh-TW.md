# Origami — 開發與驗收說明

本文件說明 MVP 的執行方式、專案結構、資料模型、登入授權、前後台頁面、開發順序與測試驗收。
產品範圍與安全基線以 `Agent.MD` 為準；本文件只描述「如何執行與如何驗證」。

**語言 / Languages：** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md)

> **第一批狀態（2026-10-28）：** 多站點、多使用者、角色權限與個人 API 憑證（PAT）已獲授權，正在實作／驗收；仍為共用應用與 PostgreSQL、單一應用實例、固定前台版型。本批僅做編譯驗證，使用者批量回歸待驗，不代表全量測試通過、已部署或已遷移正式資料庫。既有功能與歷史驗收基線不因此刪除或重新認定為本批已通過。

## 0. 功能總覽

| 領域 | 說明 |
| --- | --- |
| 管理員與使用者 | CLI 安全初始化唯一全站管理者（network admin）、後台建立一般帳號與站點成員、Cookie 登入登出（含記住我）、伺服器端密碼復原、自己帳號的 TOTP 兩步驟驗證 |
| 多站點與權限（第一批） | 獨立網域／子網域，共用應用與 PostgreSQL；內容、設定、關聯、媒體、搜尋索引與背景工作按站隔離；Reader／Editor／Administrator 站點角色與全站管理者 |
| API 憑證（第一批） | 綁定使用者與站點的 Read／Write PAT、到期與撤銷、秘密僅建立時回傳一次；BFF 與 PAT 分開授權及限流 |
| 文章 | 列表、建立、編輯、草稿、發布、撤回、置頂、排程發布、摘要、封面圖、確認後刪除與回收桶、清單批次操作（多選移入回收桶／還原／永久刪除）、清單快速編輯（就地改標題／代稱／狀態） |
| 獨立頁面 | 獨立網址、層級式導覽與層級網址（/pages/父/子/，舊平面網址自動 301）、循環防護、可作為固定首頁 |
| 分類與標籤 | 平面結構、多對多指派、描述文字、刪除只移除關聯、合併（關聯重派到目標後刪除來源） |
| 媒體 | 圖片上傳、媒體庫、Markdown 插入、alt 與說明文字、封面圖指派（文章與頁面）、引用保護刪除、響應式縮圖與 srcset、編輯器選圖器關鍵字搜尋 |
| 公開網站 | 文章列表與分頁、封面縮圖、文章與頁面詳情（含封面、說明與作者列）、相鄰與相關文章、按月歸檔、混合搜尋（可分頁）、基本導覽、行動版型 |
| 開放性 | RSS 2.0 與 Atom 1.0 訂閱（可按分類／標籤過濾、可輸出全文）、oEmbed provider（JSON＋探索連結）、sitemap.xml、robots.txt、文章級 Open Graph（og:image 以封面優先，無封面時退回本文首圖）與 canonical meta、舊網址 301 轉址、JSON 資料匯出與匯入、ZIP 完整備份（JSON＋圖片）與還原、每日自動備份（組態開啟）、`/health` 資料庫健康檢查、公開唯讀 JSON API `/api/v1` 與 MCP 端點（獨立 Api 主機；另有受限管理 API） |
| 編輯安全網 | 既有草稿每 60 秒自動儲存、修訂歷史（每篇 20 筆、與編輯器內容的逐行差異比對）、30 天回收桶與還原、草稿分享預覽連結 |
| 可見性 | 私密內容不公開，讀取須符合管理權限、密碼保護內容需解鎖後才顯示，兩者都不進入搜尋與 AI 檢索 |
| 站點設定 | 網站名稱與說明、每頁文章數、訂閱條數與全文選項、作者顯示名稱、固定首頁、網站圖示、資料匯出（JSON／ZIP）與還原式匯入 |

內容以 **Markdown** 書寫：原始 HTML 被停用，每個輸出網址都經過白名單檢查，儲存的內容無法引入腳本執行。

## 1. 環境需求

| 項目 | 版本 | 說明 |
| --- | --- | --- |
| .NET SDK | 10.0.402 | `global.json` 已鎖定；`dotnet --version` 可確認 |
| PostgreSQL | 15 以上 | 開發、測試與正式環境皆使用 PostgreSQL |
| 瀏覽器 | Edge 或 Chrome | 僅供端到端測試使用 |

領域套件版本固定於 `src/Origami.Core/Origami.Core.csproj`：

- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13
- `Markdig` 1.4.2、`HtmlSanitizer` 9.2.1039、`SkiaSharp` 4.153.1

## 2. 必要設定

連線密碼不得寫入原始碼，透過外部設定提供：

```bash
export ConnectionStrings__Cms="Host=127.0.0.1;Port=5432;Database=origami;Username=origami;Password=<密碼>"
export Storage__RootPath="D:/origami-storage"   # 可省略，預設為使用者資料目錄下的 Origami/storage
export Api__BaseUrl="http://127.0.0.1:5100"    # 網站主機（BFF）呼叫 Api 的位址
export Api__ServiceToken="<隨機長字串>"   # 僅供 BFF 的主機憑證，不等於使用者權限；Web 與 Api 必須設同一值
export Backup__Enabled=true   # 可省略，預設 false；開啟每日自動備份（Backup:Keep 保留份數，預設 7；Backup:HourUtc 執行小時，預設 3）
```

`Storage__RootPath` 必須是絕對路徑，且不得位於應用發布目錄內；未設定時使用預設位置。
資料保護金鑰以 Windows DPAPI 加密存放在全域 `Storage__RootPath/keys`，本批不移動金鑰。
預設站媒體繼續使用 `Storage__RootPath/images`；新站使用 `Storage__RootPath/sites/{SiteId:N}/images`（`N` 為無連字號 GUID）。自動備份輸出 `origami-backup-*.zip`（JSON＋圖片完整封裝）到同站 `backups/`：預設站為 `Storage__RootPath/backups/`，新站為 `Storage__RootPath/sites/{SiteId:N}/backups/`，並保留最新幾份。Web 與 Api 須使用同一持久化儲存根目錄。
`Api:ServiceToken` 僅驗證 BFF 主機，不等於任何使用者或寫入權限；管理操作還需轉發真實使用者及安全戳記並重驗角色。未設定時 BFF 認證不可用，但已簽發且有效的 PAT 仍可認證。服務通行證只透過安全外部設定提供給 Web 與 Api，絕不可分發給瀏覽器、第三方或行動端；對外 API 使用受限 PAT，而非公開 BFF 金鑰。

### 多站點選站與部署前提

- 多站共用應用與 PostgreSQL，以獨立 DNS 網域／子網域區分，保留每站 `/posts`、`/pages`、`/media` 等原有路徑，不採用路徑前綴選站。
- Api／MCP 以單一 GUID 標頭 `X-Origami-Site` 選站；缺少標頭時使用預設站 `11111111-1111-1111-1111-111111111111`。格式錯誤回傳 400，不存在或停用站回傳 404；選站標頭本身不授予權限。
- Web 經 Api 的 `/api/v1/sites/resolve?host=…` 解析請求 Host，不信任瀏覽器自填的選站標頭。未登錄 Host 一律 404；唯一例外是預設站 `HostName = null` 時，本機開發主機（`localhost`／`127.0.0.1`）仍以預設站服務，預設站綁定 Host 後連本機也不再回退。已登錄但 inactive 的 Host 一律 404，不回退。
- 登錄值只支援 DNS 名稱、IPv4 或 `localhost`；不接受協定、連接埠、路徑、萬用字元或 IPv6 literal。
- DNS、TLS 與反向代理由維運配置，本輪不操作、不宣稱已部署。部署前須規劃已登錄 Host 的路由與 HTTPS，並核對可信代理邊界。下方 HTTP 回環範例不是完整上線設定：瀏覽器的安全 Cookie 需要 HTTPS；建立帳號的 `/api/v1/admin/users` 要求 HTTPS，僅可信 BFF 從同機 loopback 連入時允許 HTTP。跨機 BFF 不適用此例外。

## 3. 執行步驟

執行前先完整備份 PostgreSQL、全部站點媒體與全域 keys。`AddSiteIsolationAndApiCredentials` 遷移已產生，但尚未套用正式資料庫；下列命令是操作說明，不是本次已執行的紀錄。遷移設計將既有內容、Settings、媒體資料及原網址歸入預設站，既有管理員保留為全站管理者，不搬動舊媒體路徑。多站點、帳號、成員或憑證實際啟用後，遷移 `Down` 會拒絕降版以避免遺失隔離及權限資料；回退須另行規劃，不得以強制降版代替還原備份。JSON／ZIP 不含帳號及 PAT，不能取代完整資料庫、媒體與 keys 備份。

```bash
# 1. 套用資料庫遷移（結構由 EF Core migrations 管理，不會刪庫重建）
dotnet run --project src/Origami.Web -- migrate

# 2. 僅初始化唯一全站管理者（密碼由互動輸入，不接受命令列參數或重導向輸入）
dotnet run --project src/Origami.Web -- admin-init

# 3. 啟動 Api 主機（網站的頁面資料也經它讀寫；必須先於網站可用）
ASPNETCORE_URLS=http://127.0.0.1:5100 dotnet run --project src/Origami.Api

# 4. 啟動網站主機（BFF 純用戶端：登入、兩步驗證與媒體檔在本機，資料讀寫經 Api）
ASPNETCORE_URLS=http://127.0.0.1:5000 dotnet run --project src/Origami.Web
```

`admin-init` 只初始化唯一全站管理者，不用於建立一般使用者；一般帳號由全站管理者透過後台建立，站點管理員管理本站成員。`admin-reset` 在伺服器端復原全站管理者帳號；重設會輪替安全戳記，使既有登入與舊 PAT 立即失效。
公開網站入口為 `/`，後台為 `/admin/posts`。
Api 主機與網站共用同一資料庫與遷移（遷移僅由 Web 的 `migrate` 命令執行）；媒體圖片仍由網站的 `/media` 提供。

## 4. 專案結構

```text
src/Origami.Core/              # 共用領域層（兩個主機共同引用）
  Data/            # DbContext、設計期工廠、migrations
  Models/          # Content、Category、Tag、MediaAsset、SiteSettings、AdminUser
  Services/        # 內容、分類標籤、媒體、Markdown、站點設定、授權、混合搜尋
  Intelligence/    # AiGateway、Embedding、SemanticSearch、Agents（MCP 工具、站內助理）
src/Origami.Web/              # 網站主機
  Components/
    Pages/          # 公開網站（Static SSR）：首頁、文章與頁面詳情、404、錯誤頁
    Admin/          # 管理後台（Interactive Server）：內容、分類標籤、媒體、設定
    Account/        # 登入、登出、身份重新驗證
    Layout/         # 公開版型與後台版型、斷線重連提示
  Services/         # 主機層服務：登入限流、兩步驗證、管理員初始化、robots/sitemap/feed
  wwwroot/          # 應用內建 CSS；持久化上傳不存於此
src/Origami.Api/              # 機器介面主機：公開 JSON、BFF／PAT 管理 API /api/v1 與唯讀 MCP /mcp
mobile/                       # 行動端閱讀客戶端（Avalonia 12，獨立 solution），見 mobile/README.md
tests/Origami.Tests/
```

## 5. 資料模型

| 模型 | 職責 | 資料庫約束 |
| --- | --- | --- |
| `AdminUser`（Identity） | 全域帳號、密碼雜湊、安全戳記、停用狀態 | `SingletonKey` 可為 null；非 null 唯一且只能為 1，僅此帳號是全站管理者，一般使用者不受單帳號限制 |
| `CmsSite` | 站點目錄、Host 映射、啟用狀態 | 站點 slug 與非 null Host 唯一 |
| `SiteMembership` | 使用者的站點角色 | `(SiteId, UserId)` 複合主鍵；Reader／Editor／Administrator |
| `ApiCredential` | 綁定使用者與站點的 PAT 雜湊、範圍、戳記、到期與撤銷時間 | 不存明文秘密；按使用者、站點及目前權限重驗 |
| `Content` | 文章與頁面（`Kind` 區分）、標題、slug、Markdown、摘要、封面參照、狀態、時間、並發版本 | `(SiteId, Slug)` 站內唯一；狀態、標題、slug、發布時間皆有檢查約束；`Version` 為並發權杖；私密與密碼保護不可同時啟用；頁面不可指向自身 |
| `Category` / `Tag` | 文章分類與標籤，含描述文字 | 名稱及 slug 的唯一性檢查限於站內同類 |
| `ContentCategories` / `ContentTags` | 內容與分類標籤的多對多關聯 | 同站關聯約束；由內容刪除時自動清理 |
| `MediaAsset` | 圖片識別、媒體類型、大小、尺寸、alt 文字、上傳時間 | `(SiteId, FileName)` 唯一；圖片本體存於該站檔案系統目錄 |
| `SiteSettings` | 每站名稱與說明、每頁文章數、訂閱條數、固定首頁、網站圖示 | 主鍵 `(SiteId, Id)`，各站仍僅允許 `Id = 1`；首頁與圖示隨內容刪除自動失效 |
| `ContentSlugRedirect` | 舊 slug 到現有內容的永久轉址記錄 | 舊 slug 站內唯一；隨內容刪除級聯清理 |
| `ContentRevision` | 每次儲存前的前一版快照（標題／本文／摘要） | 隨內容級聯刪除；每篇保留最新 20 筆 |
| `ContentView` | 每篇內容的每日瀏覽計數 | 複合主鍵（內容＋日期）；隨內容級聯刪除 |

內容另有「上層頁面」`ParentId`（僅獨立頁面）、草稿分享預覽權杖、排程時間 `PublishAt` 與回收桶時間 `DeletedAt`；
密碼保護內容另外保存 Identity 密碼雜湊，私密內容對外等同不存在。

狀態有 `Draft`、`Published` 與 `Trashed`；撤回即改回 `Draft`，刪除先進回收桶保留 30 天（可還原、逾期自動清理）。首次發布時間寫入 `PublishedAt` 後不再變更（排程發布、撤回後再發布都一樣）；排程發布以草稿＋未來時間表示，到期由背景工作晉升。

## 6. 登入與授權

- Web 使用 ASP.NET Core Identity + Cookie；Cookie 為 `__Host-` 前綴、Secure、HttpOnly、SameSite=Lax、host-only（不設定跨域 Domain）。不同站點各自登入，不提供跨站 SSO。
- 沒有公開註冊或初始化端點；CLI 只初始化／復原唯一全站管理者，一般帳號由全站管理者透過後台建立。兩步驗證僅管理自己的帳號。
- Api 註冊 IdentityCore／EF stores 供受控帳號管理，不註冊 Cookie、SignInManager、背景工作或媒體靜態檔；瀏覽器登入仍在 Web。
- 每個受保護的服務操作都會以新的 `DbContext` 重驗目前使用者、安全戳記、啟用狀態、站點成員角色與 PAT 狀態／範圍，
  不信任頁面送上的身分或角色。登出或重設密碼會輪替安全戳記，既有分頁與舊 PAT 立即失效；角色改變、成員移除、帳號／站點停用也在後續操作即時生效。
- 站點角色：`Reader` 可唯讀管理內容（含草稿讀取），不是僅能閱讀公開文章；`Editor` 可寫入內容、媒體及分類標籤；`Administrator` 另可管理本站設定、匯入匯出及成員。全站管理者（network admin）透過網站管理站點目錄和全域帳號，並擁有跨站管理權限；本批不做作者擁有權或待審稿。
- BFF 以 `Authorization: Bearer <Api:ServiceToken>` 驗證主機，管理操作同時轉發 `X-Origami-User` 與 `X-Admin-Security-Stamp` 表示真實登入身分，缺任一項回傳 401，不合成管理員身分。角色不足回傳 403，不應被當作登入失效或觸發登出；匿名網站呈現仍可由可信 BFF 代讀公開資料。
- PAT 使用 `Authorization: Bearer <PAT>`，綁定使用者與站點；有效權限是現行站點角色與範圍上限的交集：`Read` 最高 `Reader`，`Write` 最高 `Editor`。即使持有人是全站管理者，PAT 也不能管理站點／帳號／成員／憑證、站點設定或匯入匯出，不能呼叫 BFF-only 呈現、unlock／unlock-state 或瀏覽計數入口；偽造轉發身分標頭不會提權。
- PAT 固定格式為 `org_<32位小寫GUID>.<64位小寫十六進位secret>`（即 `org_GUID.secret`）；完整秘密僅建立成功時回傳一次，資料庫只存秘密雜湊。每人每站最多 20 張有效憑證，到期時間須在未來且最長 365 天；管理列表優先顯示有效憑證，再列歷史，總計最多 100 筆，不刪除歷史紀錄。
- PAT 撤銷、到期、安全戳記更新、帳號停用或成員資格移除後立即停止相應存取，每次操作重新驗證，不只依賴第一次認證。每張有效 PAT 每分鐘 120 次，並同時受每 IP 每分鐘 600 次限制；只有可信 BFF 免於機器介面匿名限流，PAT 不繼承豁免。匿名公開機器請求仍為每 IP 每分鐘 30 次，未認證管理請求為每 IP 每分鐘 600 次，失敗認證也計入。
- 登入失敗 5 次鎖定 15 分鐘；連續失敗超過 20 次時，該「帳號＋來源」組合會收到 429，
  成功登入後即重設。登入與登出皆為正常 HTTP POST，並驗證防偽權杖。
- 兩步驟驗證的第二階段同樣受帳號鎖定約束：帳號被鎖定時即使動態碼正確也無法登入；
  待驗證 Cookie 綁定安全戳記，重設密碼或變更兩步驟驗證後立即失效。
- 私密內容不公開，網站呈現須目前站點 Editor 以上，其他訪客得到 404；管理讀取依上述角色規則。私密內容不進入關鍵字搜尋、語意檢索與 MCP 工具；
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
| `/account/login` | 使用者登入（Static SSR，host-only Cookie；管理權限依目前站點角色） |

機器介面（獨立的 `Origami.Api` 主機，回應 no-store）：公開 JSON 與 MCP 仍唯讀，匿名公開請求按來源 IP 每分鐘 30 次；管理 API 支援 BFF 與受限 PAT，角色和限流見第 6 節。持 PAT 不會讓公開文章端點回傳草稿，草稿須走獲授權的管理讀取端點。

| 路由 | 內容 |
| --- | --- |
| `/api/v1/posts` | 已發布文章列表：分頁（`page`、`pageSize`）與 `category`／`tag`／`year`／`month` 篩選 |
| `/api/v1/posts/{slug}`、`/api/v1/pages/{slug}` | 文章與獨立頁面詳情（Markdown 原文）；草稿、受保護與私密內容一律 404 |
| `/api/v1/search?q=` | 混合搜尋，規則與 `/search` 相同；模型未設定時退回關鍵字 |
| `/mcp`、`/health` | 唯讀 MCP 工具（`search_posts`、`get_post`、`get_page`）與資料庫健康檢查 |
| 公開讀取面 | `/settings`、`/navigation`、`/contents/{id}/neighbors`、`/contents/{id}/related`、`/archives`、`/categories/popular`、`/taxonomies/{kind}/{slug}`、`/redirects/{slug}`、`/preview/{token}`、`/sitemap`、`/feed`、`/media/widths`——網站頁面的資料來源 |
| `/api/v1/admin/…` | 管理讀取與寫入（內容／分類標籤／媒體／設定／修訂／預覽連結／匯入匯出）；BFF 轉發真實身分，PAT 按 Read／Write 及角色上限存取內容類端點，不能管理設定或匯入匯出 |
| `/api/v1/sites/resolve?host=…` | 公開 Host 映射解析，只回傳站點中繼資料，不回傳帳號或憑證 |
| `/api/v1/admin/access`、`/sites`、`/users`、`/members`、`/credentials` | 此列除首項外均相對 `/api/v1/admin`；BFF-only 權限、站點、全域帳號、成員及 PAT 管理，服務再檢查相應角色 |
| BFF-only 呈現入口 | 已發布內容／層級頁面呈現、unlock／unlock-state、瀏覽計數仍僅供可信網站主機，PAT 不可呼叫 |

管理後台（Interactive Server，逐頁配置渲染模式）：

| 路由 | 內容 |
| --- | --- |
| `/admin/posts`、`/admin/pages` | 內容列表（含關鍵字搜尋、狀態篩選、回收桶）、快速編輯（就地改標題／代稱／狀態）、發布、撤回、確認後刪除 |
| `/admin/content/{id}` | Markdown 編輯器、預覽、分類標籤指派、媒體插入、排程發布、修訂歷史、密碼保護與私密設定 |
| `/admin/taxonomies` | 分類與標籤的建立、修改、刪除 |
| `/admin/media` | 圖片上傳、媒體庫關鍵字搜尋、alt 文字編輯、引用保護刪除 |
| `/admin/settings` | 網站名稱與說明、每頁文章數、訂閱條數、固定首頁、網站圖示、資料匯出（JSON／ZIP 完整備份）與還原式匯入 |
| `/admin/security` | 自己帳號的兩步驟驗證（TOTP）啟用與停用 |
| `/admin/sites` | 第一批：可存取站點列表／切站；全站管理者建立、更新站點及 Host／啟用狀態 |
| `/admin/users` | 第一批：全站管理者建立及啟用／停用全域一般帳號 |
| `/admin/members` | 第一批：Administrator／全站管理者管理本站成員及角色 |
| `/admin/credentials` | 第一批：目前使用者、目前站點的 PAT 列表、建立與撤銷，秘密僅顯示一次 |

新增管理路由待 Web 整合後複驗，不代表瀏覽器流程已驗收。內容讀取允許 Reader；內容／媒體／分類寫入須 Editor 以上，設定／匯入匯出／成員須 Administrator 以上，不能只靠隱藏按鈕授權。切站須開啟目標站已登錄 Host，並在該 Host 各自登入。

固定版型在行動裝置可用；操作期間停用重複提交；未儲存變更離開時會提示，既有草稿每 60 秒自動儲存；
連線中斷時明確告知「未儲存的內容尚未保存」，不會假稱已儲存。

## 8. 開發順序

1. **M1 基礎與登入**：SDK/PostgreSQL 確認、Blazor Web App、Identity、遷移、管理員初始化與恢復。
2. **M2 內容管理**：文章與頁面生命週期、分類標籤、slug 唯一、發布與撤回、樂觀並發。
3. **M3 網站可用**：圖片上傳與驗證、前台 SSR、導覽與分頁、站點設定。
4. **M4 MVP 驗收**：回歸、安全、PostgreSQL 整合、瀏覽器端到端、重啟與備份還原。

## 9. 測試與驗收

**第一批驗證邊界：只做編譯驗證；使用者最後批量回歸待驗。** 本次文件同步沒有執行命令、測試、部署或遷移。以下為既有測試涵蓋／歷史驗收基線及供後續執行的命令，不是本批全量測試報告；歷史已驗收紀錄與數字保留原適用範圍，不套用於本批。表中的舊「唯一管理員」「只有服務通行證」測試描述屬於原基線，新契約須另行回歸。

```bash
# 完整回歸（需本機有 PostgreSQL 伺服器工具，測試會自行建立隔離叢集）
dotnet test tests/Origami.Tests/Origami.Tests.csproj -c Release
```

測試一律在測試自行建立的隔離 PostgreSQL 叢集上執行（SCRAM 驗證、隨機連接埠），
不會連線到既有資料庫；缺少伺服器工具時會明確標記為未執行，不改用其他 provider 冒充通過。
`RunningSite` 現在會同時啟動 Api 主機（同庫、同服務通行證）——網頁測試驗證的是完整 BFF 鏈路。

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
| `CoverImageTests` | 封面指派與清除、公開列表與詳情帶封面、媒體說明文字（caption）上限、僅封面引用也阻擋刪除、admin API 往返、前台 og:image 與 srcset 渲染 |
| `BulkOperationsTests` | 批次移入回收桶／還原／永久刪除逐筆回報、並發衝突與不存在項目不影響其他、空清單與超上限拒絕、匿名拒絕、admin API 端點驗證 |
| `QuickEditTests` | 清單快速編輯改標題代稱與狀態、slug 退役 301、發布清除排程並同步嵌入生命週期、衝突與回收桶與非法輸入拒絕、BFF 端點驗證 |
| `ArchiveTransferTests` | ZIP 封裝打包資料與圖片、還原圖片並清理孤兒檔、缺媒體檔在動資料庫前整體拒絕、JSON 與 ZIP 分析、匯出端點 |
| `BackupServiceTests` | 每日備份落檔為有效封裝、保留份數上限清理最舊、暫存檔不殘留 |
| `AdminEfficiencyBrowserTests` | 瀏覽器驗證快速編輯、選圖器搜尋與摘要切點的互動流程 |
| `SyndicationEmbedTests` | 分類過濾 feed 與 404、Atom 1.0 輸出與過濾、篩選頁訂閱連結、oEmbed 僅公開內容、格式與路徑驗證、詳情頁探索連結 |
| `PageHierarchyTests` | TextDiff 行差異與超限回退、頁面路徑解析與未發布祖先平面回退、sitemap 層級路徑、平面網址 301、錯誤中段 404、三段式 oEmbed |
| `PresentationPolishTests` | 記住我切換持久 Cookie、Feed 全文（content:encoded／Atom content）與作者欄、byline 呈現、搜尋分頁與總數、越界頁 404、apple-touch-icon 連結與 404 |
| `HybridSearchTests` | 關鍵字命中（CJK 與大小寫）、語意結果融合去重、撤回後不再命中、前台頁呈現 |
| `SecurityAndProtectionTests` | RFC 6238 向量與 TOTP 流程、admin-reset 停用兩步驗證、密碼保護閘門與搜尋/MCP 排除、每日瀏覽計數 |
| `ProtectionRegressionTests` | 畸形解鎖 Cookie 不外洩摘要、Static SSR 解鎖表單、帳號鎖定時第二階段無法登入、私密與受保護內容不進入嵌入供應商 |
| `ImportExportRegressionTests` | 匯入拒絕越界媒體檔名、匯入還原網站圖示、縮圖變體納入引用保護 |
| `LifecycleRegressionTests` | 回收桶清理不刪除已還原內容、並行儲存不形成頁面循環、排程再發布保留首次發布時間、固定首頁仍可篩選文章列表 |
| `EditorRegressionTests` | 產生預覽連結不掩蓋其他分頁的變更、排程時間視為未儲存變更 |
| `ScheduleTimeTests` | 排程時間按瀏覽器時區換算並跨日光節約時間保持牆上時間、不存在的當地時間被拒絕 |
| `ApiV1EndpointTests` | /api/v1 列表分頁與僅已發布、詳情對草稿／受保護／私密內容 404、搜尋無模型金鑰時退回關鍵字、參數校驗與按 IP 限流 |
| `McpEndpointTests` | MCP 端點（Api 主機）僅暴露三個唯讀工具、草稿不可見、缺模型金鑰時工具層降級不回 5xx、按 IP 限流 |
| `ApiV1AdminEndpointTests` | 寫入端點無通行證或錯誤通行證一律 401、建立→發布→撤回→回收桶→還原閉環、過期版本被拒絕不靜默覆寫、管理流量獨立限流配額 |

2026-10-28 稽核修復輪已補齊並通過單項驗證：舊庫遷移升級把既有內容與 Settings 歸入預設站（`DatabaseIntegrationTests`）、PAT 限流分區與 BFF 豁免／跨憑證獨立（`TenancyApiTests`）、站點／帳號／成員管理的 BFF happy path、四個新管理頁的瀏覽器流程（`AdminTenancyBrowserTests`）、備份按站點目錄隔離（`BackupServiceTests`），以及網站主機在 Development 環境的啟動修復。其餘第一批邊界（跨 Host 隔離、遷移 Down 防護、Reader／角色寫入邊界、BFF 401／403、PAT 即時失效與數量上限、帳號自身 2FA）仍待使用者批量全量回歸；綠燈前不宣稱本批驗收完成。

### 已知限制

- 多站點、多使用者與角色權限屬於本批實作／驗收中能力，仍限單一應用實例、固定前台版型；不支援分散式／多實例部署。站點須按已登錄的獨立 DNS 網域／子網域存取（IPv4／localhost 供本機使用，不支援 IPv6 literal）；DNS／TLS／反向代理仍須維運配置。
- 所有已上傳圖片皆為公開資源，草稿中的圖片也不例外；撤回或刪除內容不會隱藏圖片網址。站點目錄隔離不等於私密附件權限。
- 資料保護金鑰以 Windows DPAPI 加密，只能由同一台電腦上的同一個使用者解密；keys 全域共用，本批不搬遷。
- 資料匯出檔（JSON 與 ZIP）包含受密碼保護內容的密碼雜湊，可離線嘗試破解，請視為機密檔案並不外流；匯入會以單一交易取代目前站內全部文字資料。上傳 JSON 時圖片檔不變動；上傳 ZIP（完整備份）時會一併還原該站圖片並清理清單外的舊圖檔（取代式語意），媒體總量上限 256 MB。此處「完整備份」僅指本站內容＋圖片封裝，不含帳號／成員／PAT 或全域 keys，不能取代完整 PostgreSQL／媒體／keys 備份。
- 公開 API 與 MCP 仍匿名可讀且唯讀；第三方／App 可用綁定使用者＋站點的 PAT 存取獲授權的管理內容，寫入最高 Editor，不可管理站點、帳號、設定或匯入匯出，不可呼叫 BFF-only 呈現／解鎖。此後端能力不表示行動客戶端已完成寫入 UI。
- 網站與 Api 為兩個程序：網站啟動前 Api 必須可用（`Api:BaseUrl`），雙方須設同一 `Api:ServiceToken`；單機部署建議相鄰連接埠，並配置 HTTPS，不能把 HTTP 回環範例當作帳號管理上線設定。
- 登入、兩步驗證、站內助理、背景工作（嵌入／回收桶清理）、資料庫遷移與管理命令仍在網站主機本地執行。
- 不含評論與外掛機制、主題系統、作者擁有權、待審稿、SSO 或私密附件；不引入 PHP 或 WordPress 資料表／REST API／外掛／主題相容層。
- `AddSiteIsolationAndApiCredentials` 未套用正式資料庫；多站／帳號／成員／憑證實際啟用後拒絕 `Down`，部署及回退前必須另行規劃完整備份與還原。
