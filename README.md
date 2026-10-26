# Origami — 开发与验收说明

本文档说明 MVP 的运行方式、项目结构、数据模型、登录授权、前后台页面、开发顺序与测试验收。
产品范围与安全基线以 `Agent.MD` 为准；本文档只描述「如何运行与如何验证」。

**语言 / Languages：** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md)

## 0. 功能总览

| 领域 | 说明 |
| --- | --- |
| 管理员 | 唯一安全初始化的管理员、Cookie 登入登出、服务器端密码恢复、TOTP 两步验证 |
| 文章 | 列表、创建、编辑、草稿、发布、撤回、置顶、排程发布、摘要、确认后删除与回收桶 |
| 独立页面 | 独立网址、层级式导航（上层页面）、循环防护、可作为固定首页 |
| 分类与标签 | 平面结构、多对多指派、描述文字、删除只移除关联 |
| 媒体 | 图片上传、媒体库、Markdown 插入、alt 文字、引用保护删除、响应式缩图与 srcset |
| 公开网站 | 文章列表与分页、文章与页面详情、相邻与相关文章、按月归档、混合搜索、基本导航、移动版式 |
| 开放性 | RSS 2.0 订阅、sitemap.xml、robots.txt、文章级 Open Graph 与 canonical meta、旧网址 301 转址、JSON 数据汇出与汇入、`/health` 数据库健康检查 |
| 编辑安全网 | 既有草稿每 60 秒自动保存、修订历史（每篇 20 笔）、30 天回收桶与还原、草稿分享预览连结 |
| 可见性 | 私密内容仅管理员可读、密码保护内容需解锁后才显示，两者都不进入搜索与 AI 检索 |
| 站点设置 | 网站名称与说明、每页文章数、订阅条数、固定首页、网站图标、JSON 资料汇出与汇入 |

内容以 **Markdown** 书写：原始 HTML 被停用，每个输出网址都经过白名单检查，储存的内容无法引入脚本执行。

## 1. 环境要求

| 项目 | 版本 | 说明 |
| --- | --- | --- |
| .NET SDK | 10.0.402 | `global.json` 已锁定；`dotnet --version` 可确认 |
| PostgreSQL | 15 以上 | 开发、测试与正式环境皆使用 PostgreSQL |
| 浏览器 | Edge 或 Chrome | 仅供端到端测试使用 |

依赖包版本固定于 `src/Origami.Web/Origami.Web.csproj`：

- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13
- `Markdig` 1.4.2、`HtmlSanitizer` 9.2.1039、`SkiaSharp` 4.153.1

## 2. 必要配置

连接密码不得写入源代码，通过外部配置提供：

```bash
export ConnectionStrings__Cms="Host=127.0.0.1;Port=5432;Database=origami;Username=origami;Password=<密码>"
export Storage__RootPath="D:/origami-storage"   # 可省略，默认位于用户数据目录下的 Origami/storage
```

`Storage__RootPath` 必须是绝对路径，且不得位于应用发布目录内；未设置时使用默认位置。
数据保护密钥以 Windows DPAPI 加密存放在 `Storage__RootPath/keys`。

## 3. 运行步骤

```bash
# 1. 应用数据库迁移（结构由 EF Core migrations 管理，不会删库重建）
dotnet run --project src/Origami.Web -- migrate

# 2. 创建唯一管理员（密码由交互输入，不接受命令行参数或重定向输入）
dotnet run --project src/Origami.Web -- admin-init

# 3. 启动网站
dotnet run --project src/Origami.Web
```

`admin-reset` 可在忘记密码时由服务器端重置；重置会轮换安全戳记，使现有登录立即失效。
公开网站入口为 `/`，后台为 `/admin/posts`。

## 4. 项目结构

```text
src/Origami.Web/
  Components/
    Pages/          # 公开网站（Static SSR）：首页、文章与页面详情、404、错误页
    Admin/          # 管理后台（Interactive Server）：内容、分类标签、媒体、设置
    Account/        # 登录、登出、身份重新验证
    Layout/         # 前后台版式、断线重连提示
  Data/             # DbContext、Identity 注册、migrations
  Models/           # Content、Category、Tag、MediaAsset、SiteSettings、AdminUser
  Services/         # 内容、分类标签、媒体、Markdown、站点设置、授权、管理命令
  wwwroot/          # 应用内置 CSS；持久化上传不存于此
tests/Origami.Tests/
```

## 5. 数据模型

| 模型 | 职责 | 数据库约束 |
| --- | --- | --- |
| `AdminUser`（Identity） | 账号、密码哈希、安全戳记 | `SingletonKey` 唯一且检查值为 1，确保只有一位管理员 |
| `Content` | 文章与页面（`Kind` 区分）、标题、slug、Markdown 正文、摘要、状态、时间、并发版本 | slug 全站唯一；状态、标题、slug、发布时间皆有检查约束；`Version` 为并发令牌；私密与密码保护不可同时启用；页面不可指向自身 |
| `Category` / `Tag` | 文章分类与标签，含描述文字 | 名称与 slug 各自唯一于同类 |
| `ContentCategories` / `ContentTags` | 内容与分类标签的多对多关联 | 由内容删除时自动清理 |
| `MediaAsset` | 图片标识、媒体类型、大小、尺寸、alt 文字、上传时间 | 文件名唯一；图片本体存于文件系统 |
| `SiteSettings` | 单站点名称与说明、每页文章数、订阅条数、固定首页、网站图标 | 仅允许 `Id = 1`；首页与图标随内容删除自动失效 |
| `ContentSlugRedirect` | 旧 slug 到现有内容的永久转址记录 | 旧 slug 唯一；随内容删除级联清理 |
| `ContentRevision` | 每次保存前的上一版快照（标题／正文／摘要） | 随内容级联删除；每篇保留最新 20 笔 |
| `ContentView` | 每篇内容的每日浏览计数 | 复合主键（内容＋日期）；随内容级联删除 |

内容另有「上层页面」`ParentId`（仅独立页面）、草稿分享预览令牌、排程时间 `PublishAt` 与回收桶时间 `DeletedAt`；
密码保护内容另外保存 Identity 密码杂凑，私密内容对外等同不存在。

状态有 `Draft`、`Published` 与 `Trashed`；撤回即改回 `Draft`，删除先进回收桶保留 30 天（可还原、逾期自动清理）。首次发布时间写入 `PublishedAt` 后不再变更（排程发布、撤回后再发布都一样）；排程发布以草稿＋未来时间表示，到期由背景工作晋升。

## 6. 登录与授权

- ASP.NET Core Identity + Cookie；Cookie 为 `__Host-` 前缀、Secure、HttpOnly、SameSite=Lax。
- 没有公开注册或初始化端点；初始化与密码恢复只能通过服务器端命令执行。
- 每个受保护的服务操作都会以新的 `DbContext` 重新确认管理员身份与安全戳记，
  不信任页面提交的任何身份或角色。登出或重置密码会轮换安全戳记，现有标签页立即失效。
- 登录失败 5 次锁定 15 分钟；连续失败超过 20 次时，该「账号＋来源」组合会收到 429，
  成功登录后即重置。登录与登出皆为正常 HTTP POST，并验证防伪令牌。
- 两步验证的第二阶段同样受账号锁定约束：账号被锁定时即使动态码正确也无法登入；
  待验证 Cookie 绑定安全戳记，重设密码或变更两步验证后立即失效。
- 私密内容仅管理员可读，其他访客得到 404，且不进入关键字搜索、语义检索与 MCP 工具；
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
| `/account/login` | 管理员登录（Static SSR，Cookie 登录） |

管理后台（Interactive Server，按页面配置渲染模式）：

| 路由 | 内容 |
| --- | --- |
| `/admin/posts`、`/admin/pages` | 内容列表（含关键字搜索、状态筛选、回收桶）、发布、撤回、确认后删除 |
| `/admin/content/{id}` | Markdown 编辑器、预览、分类标签指派、媒体插入、排程发布、修订历史、密码保护与私密设定 |
| `/admin/taxonomies` | 分类与标签的创建、修改、删除 |
| `/admin/media` | 图片上传、媒体库关键字搜索、alt 文字编辑、引用保护删除 |
| `/admin/settings` | 网站名称与说明、每页文章数、订阅条数、固定首页、网站图标、JSON 资料汇出与汇入 |
| `/admin/security` | 管理员两步验证（TOTP）启用与停用 |

固定版式在移动设备可用；操作期间禁用重复提交；未保存变更离开时会提示，既有草稿每 60 秒自动保存；
连接中断时明确告知「未保存的内容尚未保存」，不会谎称已保存。

## 8. 开发顺序

1. **M1 基础与登录**：确认 SDK/PostgreSQL、创建 Blazor Web App、Identity、迁移、管理员初始化与恢复。
2. **M2 内容管理**：文章与页面生命周期、分类标签、slug 唯一、发布与撤回、乐观并发。
3. **M3 网站可用**：图片上传与验证、前台 SSR、导航与分页、站点设置。
4. **M4 MVP 验收**：回归、安全、PostgreSQL 集成、浏览器端到端、重启与备份还原。

## 9. 测试与验收

```bash
# 完整回归（需要本机有 PostgreSQL 服务器工具，测试会自行创建隔离集群）
dotnet test tests/Origami.Tests/Origami.Tests.csproj -c Release
```

测试一律在测试自行创建的隔离 PostgreSQL 集群上执行（SCRAM 验证、随机连接端口），
不会连接到既有数据库；缺少服务器工具时会明确标记为未执行，不改用其他 provider 冒充通过。

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
| `HybridSearchTests` | 关键字命中（CJK 与大小写）、语意结果融合去重、撤回后不再命中、前台页呈现 |
| `SecurityAndProtectionTests` | RFC 6238 向量与 TOTP 流程、admin-reset 停用两步验证、密码保护闸门与搜索/MCP 排除、每日浏览计数 |
| `ProtectionRegressionTests` | 畸形解锁 Cookie 不外泄摘要、Static SSR 解锁表单、账号锁定时第二阶段无法登入、私密与受保护内容不进入嵌入供应商 |
| `ImportExportRegressionTests` | 汇入拒绝越界媒体档名、汇入还原网站图标、缩图变体纳入引用保护 |
| `LifecycleRegressionTests` | 回收桶清理不删除已还原内容、并行保存不形成页面循环、排程再发布保留首次发布时间、固定首页仍可筛选文章列表 |
| `EditorRegressionTests` | 产生预览连结不掩盖其他分页的变更、排程时间视为未储存变更 |
| `ScheduleTimeTests` | 排程时间按浏览器时区换算并跨日光节约时间保持墙上时间、不存在的当地时间被拒绝 |

### 已知限制

- 单站点、单管理员、单应用实例；不支持多用户与角色权限。
- 所有上传图片皆为公开资源，草稿中的图片也不例外；撤回或删除内容不会隐藏图片网址。
- 数据保护密钥以 Windows DPAPI 加密，只能由同一台电脑上的同一个用户解密。
- 资料汇出档包含受密码保护内容的密码杂凑，可离线尝试破解，请视为机密档案并不外流；汇入会以单一交易取代站內全部文字资料，图片档需另外还原。
- 不含评论与插件机制；多用户、多站点与主题系统不在范围内。
