# Origami

A single-site content management system for publishing articles and standalone pages, written in
**C# / ASP.NET Core Blazor** on **PostgreSQL**. It re-implements the core publishing workflow of
WordPress 7.1.3 in a modern .NET stack — no PHP, no `wp_*` table compatibility, no plugin system.

> **Scope:** single site, single administrator, single application instance, fixed public layout.
> The product scope and security baseline are defined in [`Agent.MD`](Agent.MD); this document
> describes how to run and verify the implementation.
>
> [繁體中文說明](README.zh-TW.md)

**Languages:** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md)

## Features

| Area | What it does |
| --- | --- |
| Administrator | One securely provisioned administrator, cookie sign-in, sign-out, server-side password recovery, TOTP two-factor sign-in |
| Articles | List, create, edit, draft, publish, withdraw, sticky, scheduled publishing, excerpt, confirmed permanent delete, trash with restore |
| Pages | Standalone pages with their own URLs, hierarchical navigation with a parent page, cycle protection, usable as the fixed home page |
| Categories & tags | Flat structure, many-to-many assignment, descriptions, deletion never removes articles |
| Media | Image upload, media library, insert as Markdown, alt text, reference-protected deletion, responsive variants with srcset |
| Public site | Article list with pagination, article and page detail, neighbour and related posts, monthly archives, hybrid search, basic navigation, mobile-friendly |
| Openness | RSS 2.0 feed, sitemap.xml, robots.txt, article-level Open Graph and canonical meta, 301 redirects for retired slugs, JSON data export and import, `/health` database liveness, read-only JSON API `/api/v1` and the MCP endpoint on a dedicated Api host |
| Editor safety net | Autosave every 60 s for existing drafts, revision history (20 per item), trash with 30-day retention and restore, revocable draft preview links |
| Visibility | Private content is readable by the administrator only, password-protected content needs the password, and neither enters search or AI retrieval |
| Site settings | Site name and description, articles per page, feed items, fixed home page, site icon, JSON data export and import |

Content is written in **Markdown**. Raw HTML is disabled and every rendered URL is checked against
an allow-list, so stored content cannot introduce script execution.

## Stack

| Component | Version |
| --- | --- |
| .NET SDK | 10.0.402 (pinned in `global.json`) |
| ASP.NET Core Blazor Web App | .NET 10 — public site uses Static SSR, admin uses Interactive Server |
| EF Core + Npgsql | 10.0.13 / 10.0.3 |
| ASP.NET Core Identity | 10.0.13 (cookie authentication) |
| Markdig · HtmlSanitizer · SkiaSharp | 1.4.2 · 9.2.1039 · 4.153.1 |
| Database | PostgreSQL 15 or newer |

## Getting started

### 1. Provide the configuration

Connection credentials are never stored in the source tree. Supply them through the environment:

```bash
export ConnectionStrings__Cms="Host=127.0.0.1;Port=5432;Database=origami;Username=origami;Password=<your password>"
export Storage__RootPath="D:/origami-storage"   # optional; defaults to a folder under the user profile
export Api__ServiceToken="<long random string>"   # optional; the Api host's BFF service credential for write endpoints
```

`Storage__RootPath` must be an absolute path outside the published application directory. It holds
uploaded images and the Data Protection key ring. When `Api:ServiceToken` is not set the write
endpoints stay closed. The token is equivalent to write permission: provide it only through secure
external configuration to the website host, and never expose the write endpoints directly to the
internet.

### 2. Apply the schema

```bash
dotnet run --project src/Origami.Web -- migrate
```

The schema is managed by EF Core migrations. Upgrades never drop and recreate the database.

### 3. Create the administrator

```bash
dotnet run --project src/Origami.Web -- admin-init
```

The password is read from an interactive console without echo. Credentials are never accepted as
command arguments or through redirected input, and there is no public registration or
initialization endpoint. `admin-reset` recovers access on the server and immediately revokes
existing sessions.

### 4. Run

```bash
dotnet run --project src/Origami.Web

# optional: the read-only API host for native/mobile clients and MCP
dotnet run --project src/Origami.Api
```

The public site is at `/`, the admin area at `/admin/posts`. The Api host shares the same database
and schema (migrations are applied only through the Web host's `migrate` command); uploaded images
are still served by the website at `/media`.

## Project layout

```text
src/Origami.Core/              # Shared domain layer (referenced by both hosts)
  Data/            # DbContext, design-time factory, migrations
  Models/          # Content, Category, Tag, MediaAsset, SiteSettings, AdminUser
  Services/        # Content, taxonomies, media, Markdown, settings, authorization, hybrid search
  Intelligence/    # AiGateway, Embedding, SemanticSearch, Agents (MCP tools, site assistant)
src/Origami.Web/              # Website host
  Components/
    Pages/          # Public site (Static SSR): home, article and page detail, 404, error page
    Admin/          # Admin area (Interactive Server): content, taxonomies, media, settings
    Account/        # Sign-in, sign-out, identity revalidation
    Layout/         # Public and admin layouts, reconnection notice
  Services/         # Host services: login throttling, two-factor, admin provisioning, robots/sitemap/feed
  wwwroot/          # Bundled CSS; persistent uploads never live here
src/Origami.Api/              # Machine-facing host: read-only JSON /api/v1 and MCP /mcp
mobile/                       # Mobile reader client (Avalonia 12, separate solution), see mobile/README.md
tests/Origami.Tests/
```

## Data model

| Model | Responsibility | Database constraints |
| --- | --- | --- |
| `AdminUser` (Identity) | Account, password hash, security stamp | `SingletonKey` unique and checked to be `1`, so exactly one administrator can exist |
| `Content` | Articles and pages (`Kind`), title, slug, Markdown body, excerpt, status, timestamps, version | Slug unique site-wide; check constraints on status, title, slug and publish time; `Version` is the concurrency token; private and password-protected cannot be combined; a page cannot point at itself |
| `Category` / `Tag` | Article categories and tags with descriptions | Name and slug unique within their kind |
| `ContentCategories` / `ContentTags` | Many-to-many links | Removed together with the content |
| `MediaAsset` | File identity, media type, size, dimensions, alt text, upload time | File name unique; image bytes live on disk |
| `SiteSettings` | Site name and description, articles per page, feed items, fixed home page, site icon | Only `Id = 1` is allowed; home page and icon are released with their content |
| `ContentSlugRedirect` | Retired slug to live content for permanent redirects | Old slug unique; removed with its content |
| `ContentRevision` | Previous-state snapshot taken on every save | Removed with its content; newest 20 kept |
| `ContentView` | One day of view counts per content | Composite key (content, date); removed with its content |

Content also carries a parent page (`ParentId`, pages only), the draft preview token, the scheduled
publish time `PublishAt` and the trash timestamp `DeletedAt`; password-protected content stores an
Identity password hash, and private content is indistinguishable from a missing page for visitors.

Status is `Draft`, `Published` or `Trashed`; withdrawing returns content to `Draft`, and a delete
moves it to the trash for 30 days with restore and an automatic cleanup. The first publish time is
written once and preserved across later edits, withdrawals, re-publishes and scheduled publishing.

## Authentication and authorization

- ASP.NET Core Identity with cookie authentication. Cookies use the `__Host-` prefix and are
  `Secure`, `HttpOnly` and `SameSite=Lax`.
- There is no public registration or initialization endpoint.
- Every protected server-side operation re-checks the administrator identity **and** the security
  stamp against a fresh `DbContext`. A submitted identity, role, status or content kind is never
  trusted; the stored row is authoritative for the content kind on update.
- Sign-out and password recovery rotate the security stamp, so existing interactive sessions stop
  working immediately.
- Five failed sign-ins lock the account for 15 minutes. Repeated failures beyond 20 per
  account-and-client combination receive `429`; a successful sign-in resets the counter.
- The two-factor stage obeys the same account lockout: a locked account cannot sign in even with a
  correct code, and the pending cookie is bound to the security stamp, so a password reset or a
  two-factor change ends the pending window on the server.
- Private content is readable by the signed-in administrator only; everybody else gets `404`, and
  private or protected content never enters keyword search, semantic retrieval or the MCP tools.
- Sign-in and sign-out are ordinary HTTP POSTs with antiforgery validation.
- The server rechecks the page hierarchy and locks the page rows while it saves, so two parallel
  saves cannot close a cycle.

## Public and admin pages

Public site (Static SSR, HTML rendered per request):

| Route | Content |
| --- | --- |
| `/` | Published articles by publish time with pagination; a configured fixed home page replaces the list, and a category or tag filter always shows the filtered list |
| `/posts` | Article list (redirects to `/` when no fixed home page is set) |
| `/posts/{slug}` | Article detail; unpublished or unknown slugs return `404`; includes neighbour and related articles |
| `/pages/{slug}` | Page detail; unpublished pages return `404`; retired slugs redirect permanently to the new URL |
| `/archives`, `/archives/{year}/{month}` | Monthly archive of published articles |
| `/search` | Hybrid search: keyword matching merged with pgvector results, falling back to keywords without a model |
| `/feed`, `/sitemap.xml`, `/robots.txt`, `/health`, `/favicon.ico` | RSS 2.0 feed, sitemap, crawler rules, database liveness and the site icon; published content only |
| `/preview/{token}` | Draft share preview (noindex; revoked or published links stop working, published content redirects) |
| `/account/login` | Administrator sign-in (Static SSR, cookie authentication) |

Read-only machine interface (the dedicated `Origami.Api` host; anonymous, 30 requests per minute per
source IP, `no-store` responses):

| Route | Content |
| --- | --- |
| `/api/v1/posts` | Published article list: pagination (`page`, `pageSize`) plus `category`/`tag`/`year`/`month` filters |
| `/api/v1/posts/{slug}`, `/api/v1/pages/{slug}` | Article and page detail (raw Markdown); drafts, protected and private content always return `404` |
| `/api/v1/search?q=` | Hybrid search with the same rules as `/search`; keyword-only when no model is configured |
| `/mcp`, `/health` | Read-only MCP tools (`search_posts`, `get_post`, `get_page`) and database liveness |
| `/api/v1/admin/contents…` | BFF write endpoints (admin list, edit load, create, update incl. publish/withdraw, trash, permanent delete, restore), callable only with the `Api:ServiceToken` service credential held by the website host; without a configured token they always answer 401 |

Admin area (Interactive Server, per page render mode):

| Route | Content |
| --- | --- |
| `/admin/posts`, `/admin/pages` | Content list with keyword search, status filters and the trash; publish, withdraw, confirmed delete |
| `/admin/content/{id}` | Markdown editor, preview, taxonomy assignment, media insertion, scheduled publishing, revision history, protection and privacy settings |
| `/admin/taxonomies` | Create, edit and delete categories and tags |
| `/admin/media` | Image upload, media library search, alt text, reference-protected deletion |
| `/admin/settings` | Site name and description, articles per page, feed items, fixed home page, site icon, JSON export and import |
| `/admin/security` | Administrator two-factor (TOTP) setup and removal |

## Security notes

- **Images are public.** Every uploaded image is reachable by URL, including images used only in
  drafts. Withdrawing or deleting content does not hide the image. The admin interface states this
  explicitly.
- **Uploads are validated, not trusted.** Only PNG, JPEG and WebP raster images are accepted. The
  server verifies the real format, decodes the image, enforces size and dimension limits, and
  re-encodes before storing. SVG, executables, disguised files and path traversal attempts are
  rejected, and failed uploads leave no readable partial file behind.
- **Markdown is sanitized.** Raw HTML is disabled, a tag and attribute allow-list is applied, and
  only `http`, `https` and local paths are accepted as link targets.
- **Nothing sensitive is committed.** Connection strings, passwords, tokens and Data Protection
  keys are provided through external configuration and are never written to source or logs.

## Testing

```bash
dotnet test tests/Origami.Tests/Origami.Tests.csproj -c Release
```

Tests run against a PostgreSQL cluster they create themselves (SCRAM authentication, random port)
and never touch an existing database. When the server binaries are unavailable the PostgreSQL
tests are reported as *skipped* with a diagnostic instead of silently passing on another provider.

| Test file | Coverage |
| --- | --- |
| `ContentServiceTests` | Publish lifecycle, slug uniqueness and stability, first publish time, pagination bounds, association handling, transaction rollback, optimistic concurrency, server-owned content kind, duplicate submission |
| `TaxonomyServiceTests` | Taxonomy uniqueness, pagination, deletion that only removes associations, concurrency |
| `SettingsServiceTests` | Settings persistence, output encoding, length limits, concurrent updates |
| `AdminProvisioningTests` | Single administrator, rejected duplicate and concurrent initialization, password reset that revokes old sessions |
| `ProtectedServiceTests` | Every protected service refuses anonymous or revoked identities |
| `DatabaseIntegrationTests` | Migrations, unique constraints, transactions, UTC storage, database-level rules |
| `MediaServiceTests` | Upload, format spoofing, SVG rejection, size and dimension limits, path traversal, failure cleanup, persistence |
| `MarkdownSecurityTests` | Disabled raw HTML, dangerous link and image protocols, XSS, concurrent rendering, editor preview |
| `HttpSecurityTests` | Cookie attributes, antiforgery, lockout and throttling, security headers, public 404s, admin route guards |
| `BrowserFlowTests` | Full publishing loop in a real browser, two-tab conflict, unsaved-change warning, disconnect recovery, mobile layout |
| `PersistenceTests` | Data and images survive a restart, database and file backup/restore, existing sessions keep working |
| `CommandLineTests` | Migration command, refused credential arguments and redirected input, refusal to start without configuration |
| `StoragePathsTests` | Persistent storage never inside the published directory, relative paths rejected |
| `PublicSyndicationTests` | Manual excerpt precedence and limit, robots/sitemap/feed with published-only content and immediate withdrawal, detail-page og/canonical, feed item cap |
| `PublishingParityTests` | Sticky ordering and page refusal, retired-slug 301 redirects that stop after withdrawal, lazy-loaded images, media alt text, health endpoint, JSON export and its authorization |
| `ContentLifecycleTests` | Trash removal and restore, retention purge, revision cap and restore, scheduled promotion and withdrawal, preview-token lifecycle |
| `MediaClosureTests` | Upload variants and srcset, reference-protected deletion, variant backfill |
| `HybridSearchTests` | Keyword hits (CJK and case), semantic merge without duplicates, withdrawal stops hits, public page rendering |
| `SecurityAndProtectionTests` | RFC 6238 vectors and the TOTP flow, admin-reset disabling two-factor, the protection gate with search/MCP exclusion, daily view counts |
| `ProtectionRegressionTests` | A malformed unlock cookie leaks no excerpt, the Static SSR unlock form works, a locked account cannot finish the second factor, private and protected text never reaches the embedding provider |
| `ImportExportRegressionTests` | Import refuses media names outside the image directory, import restores the site icon, responsive variants count as references |
| `LifecycleRegressionTests` | The trash purge keeps restored content, parallel saves never close a page cycle, a scheduled re-publish keeps the first publish time, a fixed home page still reaches the filtered list |
| `EditorRegressionTests` | Creating a preview link never hides another tab's change, a changed schedule counts as an unsaved change |
| `ScheduleTimeTests` | Schedule times convert through the browser time zone across daylight saving, a wall-clock time that does not exist is refused |
| `ApiV1EndpointTests` | /api/v1 list pagination and published-only content, detail `404` for drafts, protected and private items, keyword fallback without a model key, parameter validation and per-IP rate limiting |
| `McpEndpointTests` | The MCP endpoint (Api host) exposes exactly three read-only tools, drafts stay invisible, a missing key degrades at the tool level without a 5xx, per-IP rate limiting |
| `ApiV1AdminEndpointTests` | Write endpoints reject missing or wrong service tokens with 401, the create→publish→withdraw→trash→restore round trip, stale versions are rejected without silent overwrites, admin traffic has its own rate budget |

The browser tests drive real Edge or Chrome through Playwright; the persistence test performs an
actual `pg_dump` / `pg_restore` cycle.

## Known limitations

- Single site, single administrator, single application instance. No roles, multi-user or
  multi-site support.
- All uploaded images are public, including those referenced only by drafts.
- The Data Protection key ring is encrypted with Windows DPAPI, so it can only be decrypted on the
  same machine by the same user. Recovery on another machine requires re-provisioning.
- A data export contains the password hashes of protected content, which can be attacked offline.
  Treat the file as a secret and keep it out of version control and shared drives. An import
  replaces every piece of text data on the site in one transaction; image files are restored
  separately.
- The read-only API has no write endpoints and no authentication; image URLs returned to app
  clients still point at the website's `/media`, and the Api host does not handle uploads.
- The Api host and the website are two processes that are started and configured for HTTPS
  separately; for a single-instance deployment, different ports on the same machine are recommended.
- No comments, plugins or menu editor; multi-user, multi-site and theming stay out of scope.

## License

MIT — see [LICENSE](LICENSE).
