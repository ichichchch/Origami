# Origami — 开发与验收说明

本文档说明 MVP 的运行方式、项目结构、数据模型、登录授权、前后台页面、开发顺序与测试验收。
产品范围与安全基线以 `Agent.MD` 为准；本文档只描述「如何运行与如何验证」。

**语言 / Languages：** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md)

> **第一批状态（2026-10-28）：** 多站点、多用户、角色权限与个人 API 凭证（PAT）已获授权，正在实作／验收；仍为共用应用与 PostgreSQL、单一应用实例、固定前台版式。本批仅做编译验证，用户批量回归待验，不代表全量测试通过、已部署或已迁移正式数据库。既有功能与历史验收基线不因此删除或重新认定为本批已通过。

## 0. 功能总览

| 领域 | 说明 |
| --- | --- |
| 管理员与用户 | CLI 安全初始化唯一全站管理者（network admin）、后台建立普通账号与站点成员、Cookie 登入登出（含记住我）、服务器端密码恢复、自己账号的 TOTP 两步验证 |
| 多站点与权限（第一批） | 独立网域／子网域，共用应用与 PostgreSQL；内容、设置、关联、媒体、搜索索引与背景工作按站隔离；Reader／Author／Editor／Administrator 站点角色与全站管理者 |
| API 凭证（第一批） | 绑定用户与站点的 Read／Write PAT、到期与撤销、秘密仅建立时返回一次；BFF 与 PAT 分开授权及限流 |
| 文章 | 列表、创建、编辑、草稿、发布、撤回、置顶、排程发布、摘要、封面图、确认后删除与回收桶、清单批次操作（多选移入回收桶／还原／永久删除）、清单快速编辑（就地改标题／代称／状态） |
| 独立页面 | 独立网址、层级式导航与层级网址（/pages/父/子/，旧平面网址自动 301）、循环防护、可作为固定首页 |
| 分类与标签 | 平面结构、多对多指派、描述文字、删除只移除关联、合并（关联重派到目标后删除来源） |
| 媒体 | 图片上传、媒体库、Markdown 插入、alt 与说明文字、封面图指派（文章与页面）、引用保护删除、响应式缩图与 srcset、编辑器选图器关键字搜索 |
| 公开网站 | 文章列表与分页、封面缩图、文章与页面详情（含封面、说明与作者列）、相邻与相关文章、按月归档、混合搜索（可分页）、基本导航、移动版式 |
| 开放性 | RSS 2.0 与 Atom 1.0 订阅（可按分类／标签过滤、可输出全文）、oEmbed provider（JSON＋探索链接）、sitemap.xml、robots.txt、文章级 Open Graph（og:image 以封面优先，无封面时退回正文首图）与 canonical meta、旧网址 301 转址、JSON 数据汇出与汇入、ZIP 完整备份（JSON＋图片）与还原、每日自动备份（组态开启）、`/health` 数据库健康检查、公开唯读 JSON API `/api/v1` 与 MCP 端点（独立 Api 主机；另有受限管理 API） |
| 编辑安全网 | 既有草稿每 60 秒自动保存、修订历史（每篇 20 笔、与编辑器内容的逐行差异比对）、30 天回收桶与还原、草稿分享预览连结 |
| 可见性 | 私密内容不公开，读取须符合管理权限、密码保护内容需解锁后才显示，两者都不进入搜索与 AI 检索 |
| 站点设置 | 网站名称与说明、每页文章数、订阅条数与全文选项、作者显示名称、固定首页、网站图标、资料汇出（JSON／ZIP）与还原式汇入 |
| 仪表板与审计 | 后台总览（已发布文章、页面、评论审核、近 30 日浏览趋势与热门内容）与操作审计日志（发布、编辑、设置与安全操作），审计记录永久保留 |
| 内容类型与导航 | 自订内容类型（CPT）及其自订分类法／字段、站内导航编辑器、作者汇总页（/author/代称）与作者专属内容 |
| 评论与反垃圾 | 访客评论（待审／核可／垃圾／回收桶）、敏感词与蜜罐反垃圾、管理员官方回复；访客仅能经 BFF 提交评论 |
| 通知与健康 | 出站 Webhook（content.published／comment.created，HMAC-SHA256 签章、指数退避重试与人工重送）与 SiteHealth 系统健康面板（数据库、存储、排程、嵌入、Webhook、SMTP、备份、组态） |
| 站点运维 | 维护模式（回 HTTP 503，已登入成员豁免）、深色／浅色／自动色彩模式、网站标志、301 重定向管理、两步验证备用恢复码 |
| 个人资料 | 帐号个人资料汇出与涂抹（GDPR），全站管理者可代办其他帐号；其内容与评论一并匿名化 |
| 编辑辅助 | 编辑器 AI 摘要与 SEO 描述建议（未设定模型时退回本地确定性摘要）、新草稿自动储存、清单批次编辑与快速编辑 |
| 介面语言 | 介面可切换繁体中文／英文（`?lang=` 与 cookie），介面文字集中于资源档；英文翻译缺漏与格式占位符不符由测试拦截 |

内容以 **Markdown** 书写：原始 HTML 被停用，每个输出网址都经过白名单检查，储存的内容无法引入脚本执行。

## 1. 环境要求

| 项目 | 版本 | 说明 |
| --- | --- | --- |
| .NET SDK | 10.0.402 | `global.json` 已锁定；`dotnet --version` 可确认 |
| PostgreSQL | 15 以上 | 开发、测试与正式环境皆使用 PostgreSQL |
| 浏览器 | Edge 或 Chrome | 仅供端到端测试使用 |

领域依赖包版本固定于 `src/Origami.Core/Origami.Core.csproj`：

- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13
- `Markdig` 1.4.2、`HtmlSanitizer` 9.2.1039、`SkiaSharp` 4.153.1

## 2. 必要配置

连接密码不得写入源代码，通过外部配置提供：

```bash
export ConnectionStrings__Cms="Host=127.0.0.1;Port=5432;Database=origami;Username=origami;Password=<密码>"
export Storage__RootPath="D:/origami-storage"   # 可省略，默认位于用户数据目录下的 Origami/storage
export Api__BaseUrl="http://127.0.0.1:5100"    # 网站主机（BFF）调用 Api 的地址
export Api__ServiceToken="<随机长字符串>"   # 仅供 BFF 的主机凭证，不等于用户权限；Web 与 Api 必须设同一值
export Backup__Enabled=true   # 可省略，默认 false；开启每日自动备份（Backup:Keep 保留份数，默认 7；Backup:HourUtc 执行小时，默认 3）
```

`Storage__RootPath` 必须是绝对路径，且不得位于应用发布目录内；未设置时使用默认位置。
数据保护密钥以 Windows DPAPI 加密存放在全局 `Storage__RootPath/keys`，本批不移动密钥。
默认站媒体继续使用 `Storage__RootPath/images`；新站使用 `Storage__RootPath/sites/{SiteId:N}/images`（`N` 为无连字符 GUID）。自动备份输出 `origami-backup-*.zip`（JSON＋图片完整封装）到同站 `backups/`：默认站为 `Storage__RootPath/backups/`，新站为 `Storage__RootPath/sites/{SiteId:N}/backups/`，并保留最新几份。Web 与 Api 须使用同一持久化存储根目录。
`Api:ServiceToken` 仅验证 BFF 主机，不等价于任何用户或写入权限；管理操作还需转发真实用户及安全戳记并重验角色。未设置时 BFF 认证不可用，但已签发且有效的 PAT 仍可认证。服务通行证只通过安全外部配置提供给 Web 与 Api，绝不可分发给浏览器、第三方或移动端；对外 API 使用受限 PAT，而非公开 BFF 密钥。

### 多站点选站与部署前提

- 多站共用应用与 PostgreSQL，以独立 DNS 网域／子网域区分，保留每站 `/posts`、`/pages`、`/media` 等原有路径，不采用路径前缀选站。
- Api／MCP 以单一 GUID 标头 `X-Origami-Site` 选站；缺少标头时使用默认站 `11111111-1111-1111-1111-111111111111`。格式错误返回 400，不存在或停用站返回 404；选站标头本身不授予权限。
- Web 经 Api 的 `/api/v1/sites/resolve?host=…` 解析请求 Host，不信任浏览器自填的选站标头。未登记 Host 一律 404；唯一例外是默认站 `HostName = null` 时，本机开发主机（`localhost`／`127.0.0.1`）仍以默认站服务，默认站绑定 Host 后连本机也不再回退。已登记但 inactive 的 Host 一律 404，不回退。
- 登记值只支持 DNS 名称、IPv4 或 `localhost`；不接受协议、端口、路径、通配符或 IPv6 literal。
- DNS、TLS 与反向代理由运维配置，本轮不操作、不宣称已部署。部署前须规划已登记 Host 的路由与 HTTPS，并核对可信代理边界。下方 HTTP 回环示例不是完整上线配置：浏览器的安全 Cookie 需要 HTTPS；创建账号的 `/api/v1/admin/users` 要求 HTTPS，仅可信 BFF 从同机 loopback 连入时允许 HTTP。跨机 BFF 不适用此例外。

## 3. 运行步骤

执行前先完整备份 PostgreSQL、全部站点媒体与全局 keys。`AddSiteIsolationAndApiCredentials` 迁移已产生，但尚未套用正式数据库；下列命令是操作说明，不是本次已执行的记录。迁移设计将既有内容、Settings、媒体资料及原网址归入默认站，既有管理员保留为全站管理者，不搬动旧媒体路径。多站点、账号、成员或凭证实际启用后，迁移 `Down` 会拒绝降版以避免丢失隔离及权限资料；回退须另行规划，不得以强制降版代替恢复备份。JSON／ZIP 不含账号及 PAT，不能取代完整数据库、媒体与 keys 备份。

```bash
# 1. 应用数据库迁移（结构由 EF Core migrations 管理，不会删库重建）
dotnet run --project src/Origami.Web -- migrate

# 2. 仅初始化唯一全站管理者（密码由交互输入，不接受命令行参数或重定向输入）
dotnet run --project src/Origami.Web -- admin-init

# 3. 启动 Api 主机（网站的页面数据也经它读写；必须先于网站可用）
ASPNETCORE_URLS=http://127.0.0.1:5100 dotnet run --project src/Origami.Api

# 4. 启动网站主机（BFF 纯客户端：登录、两步验证与媒体文件在本机，数据读写经 Api）
ASPNETCORE_URLS=http://127.0.0.1:5000 dotnet run --project src/Origami.Web
```

`admin-init` 只初始化唯一全站管理者，不用于创建普通用户；普通账号由全站管理者通过后台建立，站点管理员管理本站成员。`admin-reset` 在服务器端恢复全站管理者账号；重置会轮换安全戳记，使现有登录与旧 PAT 立即失效。
公开网站入口为 `/`，后台为 `/admin/posts`。
Api 主机与网站共用同一数据库与迁移（迁移仅由 Web 的 `migrate` 命令执行）；媒体图片仍由网站的 `/media` 提供。

## 4. 项目结构

```text
src/Origami.Core/              # 共用领域层（两个主机共同引用）
  Data/            # DbContext、设计期工厂、migrations
  Models/          # Content、Category、Tag、MediaAsset、SiteSettings、AdminUser
  Services/        # 内容、分类标签、媒体、Markdown、站点设置、授权、混合搜索
  Intelligence/    # AiGateway、Embedding、SemanticSearch、Agents（MCP 工具、站内助理）
src/Origami.Web/              # 网站主机
  Components/
    Pages/          # 公开网站（Static SSR）：首页、文章与页面详情、404、错误页
    Admin/          # 管理后台（Interactive Server）：内容、分类标签、媒体、设置
    Account/        # 登录、登出、身份重新验证
    Layout/         # 前后台版式、断线重连提示
  Services/         # 主机层服务：登录限流、两步验证、管理员初始化、robots/sitemap/feed
  wwwroot/          # 应用内置 CSS；持久化上传不存于此
src/Origami.Api/              # 机器接口主机：公开 JSON、BFF／PAT 管理 API /api/v1 与唯读 MCP /mcp
mobile/                       # 行动端阅读客户端（Avalonia 12，独立 solution），见 mobile/README.md
tests/Origami.Tests/
```

## 5. 数据模型

| 模型 | 职责 | 数据库约束 |
| --- | --- | --- |
| `AdminUser`（Identity） | 全局账号、密码哈希、安全戳记、停用状态 | `SingletonKey` 可为 null；非 null 唯一且只能为 1，仅此账号是全站管理者，普通用户不受单账号限制 |
| `CmsSite` | 站点目录、Host 映射、启用状态 | 站点 slug 与非 null Host 唯一 |
| `SiteMembership` | 用户的站点角色 | `(SiteId, UserId)` 复合主键；Reader／Author／Editor／Administrator |
| `ApiCredential` | 绑定用户与站点的 PAT 哈希、范围、戳记、到期与撤销时间 | 不存明文秘密；按用户、站点及当前权限重验 |
| `Content` | 文章与页面（`Kind` 区分）、标题、slug、Markdown 正文、摘要、封面参照、状态、时间、并发版本 | `(SiteId, Slug)` 站内唯一；状态、标题、slug、发布时间皆有检查约束；`Version` 为并发令牌；私密与密码保护不可同时启用；页面不可指向自身 |
| `Category` / `Tag` | 文章分类与标签，含描述文字 | 名称及 slug 的唯一性检查限于站内同类 |
| `ContentCategories` / `ContentTags` | 内容与分类标签的多对多关联 | 同站关联约束；由内容删除时自动清理 |
| `MediaAsset` | 图片标识、媒体类型、大小、尺寸、alt 文字、上传时间 | `(SiteId, FileName)` 唯一；图片本体存于该站文件系统目录 |
| `SiteSettings` | 每站名称与说明、每页文章数、订阅条数、固定首页、网站图标 | 主键 `(SiteId, Id)`，各站仍仅允许 `Id = 1`；首页与图标随内容删除自动失效 |
| `ContentSlugRedirect` | 旧 slug 到现有内容的永久转址记录 | 旧 slug 站内唯一；随内容删除级联清理 |
| `ContentRevision` | 每次保存前的上一版快照（标题／正文／摘要） | 随内容级联删除；每篇保留最新 20 笔 |
| `ContentView` | 每篇内容的每日浏览计数 | 复合主键（内容＋日期）；随内容级联删除 |

内容另有「上层页面」`ParentId`（仅独立页面）、草稿分享预览令牌、排程时间 `PublishAt` 与回收桶时间 `DeletedAt`；
密码保护内容另外保存 Identity 密码杂凑，私密内容对外等同不存在。

状态有 `Draft`、`Published` 与 `Trashed`；撤回即改回 `Draft`，删除先进回收桶保留 30 天（可还原、逾期自动清理）。首次发布时间写入 `PublishedAt` 后不再变更（排程发布、撤回后再发布都一样）；排程发布以草稿＋未来时间表示，到期由背景工作晋升。

## 6. 登录与授权

- Web 使用 ASP.NET Core Identity + Cookie；Cookie 为 `__Host-` 前缀、Secure、HttpOnly、SameSite=Lax、host-only（不设置跨域 Domain）。不同站点各自登录，不提供跨站 SSO。
- 没有公开注册或初始化端点；CLI 只初始化／恢复唯一全站管理者，普通账号由全站管理者通过后台建立。两步验证仅管理自己的账号。
- Api 注册 IdentityCore／EF stores 供受控账号管理，不注册 Cookie、SignInManager、背景工作或媒体静态文件；浏览器登录仍在 Web。
- 每个受保护的服务操作都会以新的 `DbContext` 重验当前用户、安全戳记、启用状态、站点成员角色与 PAT 状态／范围，
  不信任页面提交的身份或角色。登出或重置密码会轮换安全戳记，现有标签页与旧 PAT 立即失效；角色改变、成员移除、账号／站点停用也在后续操作即时生效。
- 站点角色：`Reader` 可唯读管理内容（含草稿读取），不是仅能阅读公开文章；`Author` 可建立内容并提交待审，但不能自行发布、也不能编辑他人内容；`Editor` 可写入内容、媒体及分类标签；`Administrator` 另可管理本站设置、汇入汇出及成员。全站管理者（network admin）通过网站管理站点目录和全局账号，并拥有跨站管理权限。
- BFF 以 `Authorization: Bearer <Api:ServiceToken>` 验证主机，管理操作同时转发 `X-Origami-User` 与 `X-Admin-Security-Stamp` 表示真实登录身份，缺任一项返回 401，不合成管理员身份。角色不足返回 403，不应被当作登录失效或触发登出；匿名网站呈现仍可由可信 BFF 代读公开数据。
- PAT 使用 `Authorization: Bearer <PAT>`，绑定用户与站点；有效权限是现行站点角色与范围上限的交集：`Read` 最高 `Reader`，`Write` 最高 `Editor`。即使持有人是全站管理者，PAT 也不能管理站点／账号／成员／凭证、站点设置或汇入汇出，不能调用 BFF-only 呈现、unlock／unlock-state 或浏览计数入口；伪造转发身份标头不会提权。
- PAT 固定格式为 `org_<32位小写GUID>.<64位小写十六进制secret>`（即 `org_GUID.secret`）；完整秘密仅创建成功时返回一次，数据库只存秘密哈希。每人每站最多 20 张有效凭证，到期时间须在未来且最长 365 天；管理列表优先显示有效凭证，再列历史，总计最多 100 笔，不删除历史记录。
- PAT 撤销、到期、安全戳记更新、账号停用或成员资格移除后立即停止相应存取，每次操作重新验证，不只依赖第一次认证。每张有效 PAT 每分钟 120 次，并同时受每 IP 每分钟 600 次限制；只有可信 BFF 免于机器接口匿名限流，PAT 不继承豁免。匿名公开机器请求仍为每 IP 每分钟 30 次，未认证管理请求为每 IP 每分钟 600 次，失败认证也计入。
- 登录失败 5 次锁定 15 分钟；连续失败超过 20 次时，该「账号＋来源」组合会收到 429，
  成功登录后即重置。登录与登出皆为正常 HTTP POST，并验证防伪令牌。
- 两步验证的第二阶段同样受账号锁定约束：账号被锁定时即使动态码正确也无法登入；
  待验证 Cookie 绑定安全戳记，重设密码或变更两步验证后立即失效。
- 私密内容不公开，网站呈现须当前站点 Editor 以上，其他访客得到 404；管理读取依上述角色规则。私密内容不进入关键字搜索、语义检索与 MCP 工具；
  密码保护内容需在页面输入密码解锁，正文与摘要不进入索引与搜索。
- 内容类型由服务器决定：创建时明确指定，更新时以数据库既有类型为准，提交不同类型会被拒绝。
- 独立页面的上层关系由服务器复查并锁定页面列，跨分页并行保存不会形成循环层级。

## 7. 前后台页面

公开网站（Static SSR，按请求读取数据输出 HTML）：

| 路由 | 内容 |
| --- | --- |
| `/` | 已发布文章列表，按发布时间倒序，含分页；站点指定固定首页时改显示该页面，附带分类／标签筛选时显示筛选后的列表 |
| `/posts` | 文章列表页（未指定固定首页时会转址到 `/`） |
| `/posts/{slug}` | 文章详情；未发布或不存在返回 404；含相邻文章与语义相关文章 |
| `/pages/{slug}` | 独立页面详情；未发布返回 404；旧网址在 slug 变更后 301 转址到新网址 |
| `/archives`、`/archives/{年}/{月}` | 按月归档的公开文章一览 |
| `/search` | 混合搜索：关键字比对结合 pgvector 语义结果，模型未设定时退回关键字 |
| `/feed`、`/sitemap.xml`、`/robots.txt`、`/health`、`/favicon.ico` | RSS 2.0 订阅、站点地图、爬虫规则、数据库健康检查与网站图标；仅反映已发布状态 |
| `/preview/{token}` | 草稿分享预览（noindex；撤销或发布后失效，已发布则转址公开网址） |
| `/account/login` | 用户登录（Static SSR，host-only Cookie；管理权限依当前站点角色） |

机器接口（独立的 `Origami.Api` 主机，响应 no-store）：公开 JSON 与 MCP 仍唯读，匿名公开请求按来源 IP 每分钟 30 次；管理 API 支持 BFF 与受限 PAT，角色和限流见第 6 节。持 PAT 不会让公开文章端点返回草稿，草稿须走获授权的管理读取端点。

| 路由 | 内容 |
| --- | --- |
| `/api/v1/posts` | 已发布文章列表：分页（`page`、`pageSize`）与 `category`／`tag`／`year`／`month` 筛选 |
| `/api/v1/posts/{slug}`、`/api/v1/pages/{slug}` | 文章与独立页面详情（Markdown 原文）；草稿、受保护与私密内容一律 404 |
| `/api/v1/search?q=` | 混合搜索，规则与 `/search` 相同；模型未设定时退回关键字 |
| `/mcp`、`/health` | 唯读 MCP 工具（`search_posts`、`get_post`、`get_page`）与数据库健康检查 |
| 公开读取面 | `/settings`、`/navigation`、`/contents/{id}/neighbors`、`/contents/{id}/related`、`/archives`、`/categories/popular`、`/taxonomies/{kind}/{slug}`、`/redirects/{slug}`、`/preview/{token}`、`/sitemap`、`/feed`、`/media/widths`——网站页面的数据来源 |
| `/api/v1/admin/…` | 管理读取与写入（内容／分类标签／媒体／设置／修订／预览连结／汇入汇出）；BFF 转发真实身份，PAT 按 Read／Write 及角色上限存取内容类端点，不能管理设置或汇入汇出 |
| `/api/v1/sites/resolve?host=…` | 公开 Host 映射解析，只返回站点中继资料，不返回账号或凭证 |
| `/api/v1/admin/access`、`/sites`、`/users`、`/members`、`/credentials` | 此列除首项外均相对 `/api/v1/admin`；BFF-only 权限、站点、全局账号、成员及 PAT 管理，服务再检查相应角色 |
| BFF-only 呈现入口 | 已发布内容／层级页面呈现、unlock／unlock-state、浏览计数仍仅供可信网站主机，PAT 不可调用 |

管理后台（Interactive Server，按页面配置渲染模式）：

| 路由 | 内容 |
| --- | --- |
| `/admin/posts`、`/admin/pages` | 内容列表（含关键字搜索、状态筛选、回收桶）、快速编辑（就地改标题／代称／状态）、发布、撤回、确认后删除 |
| `/admin/content/{id}` | Markdown 编辑器、预览、分类标签指派、媒体插入、排程发布、修订历史、密码保护与私密设定 |
| `/admin/taxonomies` | 分类与标签的创建、修改、删除 |
| `/admin/media` | 图片上传、媒体库关键字搜索、alt 文字编辑、引用保护删除 |
| `/admin/settings` | 网站名称与说明、每页文章数、订阅条数、固定首页、网站图标、资料汇出（JSON／ZIP 完整备份）与还原式汇入 |
| `/admin/security` | 自己账号的两步验证（TOTP）启用与停用 |
| `/admin/sites` | 第一批：可访问站点列表／切站；全站管理者建立、更新站点及 Host／启用状态 |
| `/admin/users` | 第一批：全站管理者建立及启用／停用全局普通账号 |
| `/admin/members` | 第一批：Administrator／全站管理者管理本站成员及角色 |
| `/admin/credentials` | 第一批：当前用户、当前站点的 PAT 列表、建立与撤销，秘密仅显示一次 |

新增管理路由待 Web 整合后复验，不代表浏览器流程已验收。内容读取允许 Reader；内容／媒体／分类写入须 Editor 以上，设置／汇入汇出／成员须 Administrator 以上，不能只靠隐藏按钮授权。切站须打开目标站已登记 Host，并在该 Host 各自登录。

固定版式在移动设备可用；操作期间禁用重复提交；未保存变更离开时会提示，既有草稿每 60 秒自动保存；
连接中断时明确告知「未保存的内容尚未保存」，不会谎称已保存。

## 8. 开发顺序

1. **M1 基础与登录**：确认 SDK/PostgreSQL、创建 Blazor Web App、Identity、迁移、管理员初始化与恢复。
2. **M2 内容管理**：文章与页面生命周期、分类标签、slug 唯一、发布与撤回、乐观并发。
3. **M3 网站可用**：图片上传与验证、前台 SSR、导航与分页、站点设置。
4. **M4 MVP 验收**：回归、安全、PostgreSQL 集成、浏览器端到端、重启与备份还原。

## 9. 测试与验收

**第一批验证边界：只做编译验证；用户最后批量回归待验。** 本次文件同步没有执行命令、测试、部署或迁移。以下为既有测试覆盖／历史验收基线及供后续执行的命令，不是本批全量测试报告；历史已验收记录与数字保留原适用范围，不套用于本批。表中的旧「唯一管理员」「只有服务通行证」测试描述属于原基线，新契约须另行回归。

```bash
# 完整回归（需要本机有 PostgreSQL 服务器工具，测试会自行创建隔离集群）
dotnet test tests/Origami.Tests/Origami.Tests.csproj -c Release
```

测试一律在测试自行创建的隔离 PostgreSQL 集群上执行（SCRAM 验证、随机连接端口），
不会连接到既有数据库；缺少服务器工具时会明确标记为未执行，不改用其他 provider 冒充通过。
`RunningSite` 现在会同时启动 Api 主机（同库、同服务通行证）——网页测试验证的是完整 BFF 链路。

| 测试文件 | 覆盖范围 |
| --- | --- |
| `ContentServiceTests` | 发布闭环、slug 唯一与稳定性、首次发布时间、分页边界、关联去重与删除、事务回滚、乐观并发、内容类型由服务器决定、重复提交被唯一约束拒绝 |
| `TaxonomyServiceTests` | 分类标签唯一性、分页、删除仅移除关联、乐观并发 |
| `SettingsServiceTests` | 站点设置存储、输出编码、长度上限、并发更新 |
| `AdminProvisioningTests` | 唯一管理员、重复与并发初始化被拒绝、密码重置与旧登录失效、取消操作不改变数据 |
| `ProtectedServiceTests` | 未登录或身份失效时，所有受保护服务都拒绝操作 |
| `DatabaseIntegrationTests` | 迁移、唯一约束、事务、UTC 时间保存、数据库层级规则 |
| `MediaServiceTests` | 图片上传、格式伪装、SVG 拒绝、大小与尺寸上限、路径穿越、失败清理、持久化 |
| `MarkdownSecurityTests` | 原始 HTML 禁用、危险链接与图片网址协议、XSS 防护、并发渲染、编辑器预览 |
| `HttpSecurityTests` | Cookie 属性、防伪、登录失败锁定与速率限制、安全响应头、公开路由 404、撤回与删除后不可读取、未登录无法进入任何后台页面 |
| `BrowserFlowTests` | 完整发布闭环、双标签页并发冲突、未保存提示、断线恢复、移动版式 |
| `PersistenceTests` | 应用重启后数据与图片可用、数据库与文件备份还原、原登录仍可用 |
| `CommandLineTests` | 迁移命令应用结构且可重复执行、管理命令拒绝凭据参数与重定向输入、缺少设置时拒绝启动 |
| `StoragePathsTests` | 持久化目录不得位于发布目录内、相对路径拒绝、默认位置在应用目录之外 |
| `PublicSyndicationTests` | 手写摘要优先与上限、robots/sitemap/feed 仅含已发布内容且撤回后立即下架、详情页 og/canonical、feed 条数上限 |
| `PublishingParityTests` | 置顶排序与页面禁用置顶、旧 slug 301 转址与撤回后停止、图片 lazy loading、媒体 alt 文字、健康检查、JSON 导出与权限 |
| `ContentLifecycleTests` | 回收桶下架与还原、逾期清理、修订版本数量上限与还原、排程晋升与撤回、预览链接生命周期 |
| `MediaClosureTests` | 上传缩图变体与 srcset、引用保护删除、变体回填 |
| `CoverImageTests` | 封面指派与清除、公开列表与详情带封面、媒体说明文字（caption）上限、仅封面引用也阻挡删除、admin API 往返、前台 og:image 与 srcset 渲染 |
| `BulkOperationsTests` | 批次移入回收桶／还原／永久删除逐笔回报、并冲突与不存在项目不影响其他、空清单与超上限拒绝、匿名拒绝、admin API 端点验证 |
| `QuickEditTests` | 清单快速编辑改标题代称与状态、slug 退役 301、发布清除排程并同步嵌入生命周期、冲突与回收桶与非法输入拒绝、BFF 端点验证 |
| `ArchiveTransferTests` | ZIP 封装打包资料与图片、还原图片并清理孤儿档、缺媒体档在动资料库前整体拒绝、JSON 与 ZIP 分析、汇出端点 |
| `BackupServiceTests` | 每日备份落档为有效封装、保留份数上限清理最旧、暂存档不残留 |
| `AdminEfficiencyBrowserTests` | 浏览器验证快速编辑、选图器搜索与摘要切点的互动流程 |
| `SyndicationEmbedTests` | 分类过滤 feed 与 404、Atom 1.0 输出与过滤、筛选页订阅连结、oEmbed 仅公开内容、格式与路径验证、详情页探索链接 |
| `PageHierarchyTests` | TextDiff 行差异与超限回退、页面路径解析与未发布祖先平面回退、sitemap 层级路径、平面网址 301、错误中段 404、三段式 oEmbed |
| `PresentationPolishTests` | 记住我切换持久 Cookie、Feed 全文（content:encoded／Atom content）与作者栏、byline 呈现、搜寻分页与总数、越界页 404、apple-touch-icon 链接与 404 |
| `HybridSearchTests` | 关键字命中（CJK 与大小写）、语意结果融合去重、撤回后不再命中、前台页呈现 |
| `SecurityAndProtectionTests` | RFC 6238 向量与 TOTP 流程、admin-reset 停用两步验证、密码保护闸门与搜索/MCP 排除、每日浏览计数 |
| `ProtectionRegressionTests` | 畸形解锁 Cookie 不外泄摘要、Static SSR 解锁表单、账号锁定时第二阶段无法登入、私密与受保护内容不进入嵌入供应商 |
| `ImportExportRegressionTests` | 汇入拒绝越界媒体档名、汇入还原网站图标、缩图变体纳入引用保护 |
| `LifecycleRegressionTests` | 回收桶清理不删除已还原内容、并行保存不形成页面循环、排程再发布保留首次发布时间、固定首页仍可筛选文章列表 |
| `EditorRegressionTests` | 产生预览连结不掩盖其他分页的变更、排程时间视为未储存变更 |
| `ScheduleTimeTests` | 排程时间按浏览器时区换算并跨日光节约时间保持墙上时间、不存在的当地时间被拒绝 |
| `ApiV1EndpointTests` | /api/v1 列表分页与仅已发布、详情对草稿／受保护／私密内容 404、搜索无模型金钥时退回关键字、参数校验与按 IP 限流 |
| `McpEndpointTests` | MCP 端点（Api 主机）仅暴露三个唯读工具、草稿不可见、缺模型金钥时工具层降级不返回 5xx、按 IP 限流 |
| `ApiV1AdminEndpointTests` | 写入端点无通行证或错误通行证一律 401、建立→发布→撤回→回收桶→还原闭环、过期版本被拒绝不静默覆写、管理流量独立限流配额 |

2026-10-28 审计修复轮已补齐并通过单项验证：旧库迁移升级把既有内容与 Settings 归入默认站（`DatabaseIntegrationTests`）、PAT 限流分区与 BFF 豁免／跨凭证独立（`TenancyApiTests`）、站点／账号／成员管理的 BFF happy path、四个新管理页的浏览器流程（`AdminTenancyBrowserTests`）、备份按站点目录隔离（`BackupServiceTests`），以及 Web 主机在 Development 环境的启动修复。其余第一批边界（跨 Host 隔离、迁移 Down 防护、Reader／角色写入边界、BFF 401／403、PAT 即时失效与数量上限、账号自身 2FA）仍待使用者批量全量回归；绿灯前不宣称本批验收完成。

### 已知限制

- 多站点、多用户与角色权限属于本批实作／验收中能力，仍限单一应用实例、固定前台版式；不支持分散式／多实例部署。站点须按已登记的独立 DNS 网域／子网域访问（IPv4／localhost 供本机使用，不支持 IPv6 literal）；DNS／TLS／反向代理仍须运维配置。
- 所有上传图片皆为公开资源，草稿中的图片也不例外；撤回或删除内容不会隐藏图片网址。站点目录隔离不等于私密附件权限。
- 数据保护密钥以 Windows DPAPI 加密，只能由同一台电脑上的同一个用户解密；keys 全局共用，本批不搬迁。
- 资料汇出档（JSON 与 ZIP）包含受密码保护内容的密码杂凑，可离线尝试破解，请视为机密档案并不外流；汇入会以单一交易取代当前站內全部文字资料。上传 JSON 时图片档不变动；上传 ZIP（完整备份）时会一并还原该站图片并清理清单外的旧图档（取代式语义），媒体总量上限 256 MB。此处「完整备份」仅指本站内容＋图片封装，不含账号／成员／PAT 或全局 keys，不能取代完整 PostgreSQL／媒体／keys 备份。
- 公开 API 与 MCP 仍匿名可读且唯读；第三方／App 可用绑定用户＋站点的 PAT 存取获授权的管理内容，写入最高 Editor，不可管理站点、账号、设置或汇入汇出，不可调用 BFF-only 呈现／解锁。此后端能力不表示移动客户端已完成写入 UI。
- 网站与 Api 为两个进程：网站启动前 Api 必须可用（`Api:BaseUrl`），双方须设同一 `Api:ServiceToken`；单机部署建议相邻端口，并配置 HTTPS，不能把 HTTP 回环示例当作账号管理上线配置。
- 登录、两步验证、站内助理、背景工作（嵌入／回收桶清理）、数据库迁移与管理命令仍在网站主机本地执行。
- 不含评论与插件机制、主题系统、作者拥有权、待审稿、SSO 或私密附件；不引入 PHP 或 WordPress 资料表／REST API／插件／主题兼容层。
- `AddSiteIsolationAndApiCredentials` 未套用正式数据库；多站／账号／成员／凭证实际启用后拒绝 `Down`，部署及回退前必须另行规划完整备份与恢复。
