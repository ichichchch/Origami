# Origami

A content management system for publishing articles and standalone pages, written in
**C# / ASP.NET Core Blazor** on **PostgreSQL**. It re-implements the core publishing workflow of
WordPress 7.1.3 in a modern .NET stack — no PHP, no `wp_*` table compatibility, no plugin system.

> **Scope:** multiple sites and users sharing one application and PostgreSQL database, a single
> application instance and a fixed public layout. The first batch (2026-10-28) adds site isolation,
> roles and personal API tokens (PATs), and is still under implementation/acceptance. This batch
> is limited to compilation verification; the user's batch regression run is pending. This is not
> a claim of full test success, deployment or a production database migration. Existing features
> and historical acceptance baselines are retained, not reclassified as passing this batch.
> The product scope and security baseline are defined in [`Agent.MD`](Agent.MD); this document
> describes how to run and verify the implementation.
>
> [繁體中文說明](README.zh-TW.md)

**Languages:** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md)

## Features

| Area | What it does |
| --- | --- |
| Administrators & users | CLI bootstrap of the unique network admin, ordinary accounts and site memberships managed in the admin area, cookie sign-in with "remember me", sign-out, server-side password recovery, TOTP for one's own account |
| Sites & roles (first batch) | Independent domains/subdomains sharing the application and PostgreSQL; site-isolated content, settings, relations, media, search indexes and background work; Reader/Editor/Administrator site roles and network admin |
| API credentials (first batch) | User-and-site-bound Read/Write PATs, expiration and revocation, a secret returned only once on creation; BFF and PAT authorization and rate limits are separate |
| Articles | List, create, edit, draft, publish, withdraw, sticky, scheduled publishing, excerpt, cover image, confirmed permanent delete, trash with restore, list bulk actions (trash/restore/delete many), list quick edit (title/slug/status in place) |
| Pages | Standalone URLs, hierarchical navigation with hierarchical URLs (/pages/parent/child/, flat URLs redirect 301), cycle protection, usable as the fixed home page |
| Categories & tags | Flat structure, many-to-many assignment, descriptions, deletion never removes articles, merge (reassign relations to a target, then remove the source) |
| Media | Image upload, media library, insert as Markdown, alt text and caption, cover image assignment (posts and pages), reference-protected deletion, responsive variants with srcset, keyword search in the editor picker |
| Public site | Article list with pagination and cover thumbnails, article and page detail with cover, caption and author byline, neighbour and related posts, monthly archives, paginated hybrid search, basic navigation, mobile-friendly |
| Openness | RSS 2.0 and Atom 1.0 feeds (filterable by category/tag, optional full text), oEmbed provider (JSON + discovery links), sitemap.xml, robots.txt, article-level Open Graph (og:image prefers the cover, falls back to the first inline image) and canonical meta, 301 redirects for retired slugs, JSON data export and import, ZIP full backup (JSON + images) with restore, daily automatic backup (opt-in via configuration), `/health` database liveness, public read-only JSON API `/api/v1` and the MCP endpoint on a dedicated Api host, alongside restricted management APIs |
| Editor safety net | Autosave every 60 s for existing drafts, revision history (20 per item with line-diff against the editor), trash with 30-day retention and restore, revocable draft preview links |
| Visibility | Private content is not public and requires management authorization, password-protected content needs the password, and neither enters search or AI retrieval |
| Site settings | Site name and description, articles per page, feed items with a full-text option, author display name, fixed home page, site icon, data export (JSON / ZIP full backup) with replace-style import |
| Appearance & themes | Controlled theme presets (reading serif / minimal compact / gallery display — real differences in fonts and layout density), accent color and column width fine-tuning, live preview on the settings page, automatic foreground lightening in dark mode, a per-admin three-state appearance toggle remembered via cookie (takes precedence over system detection when the site is set to auto), prefers-contrast support |

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
export Api__BaseUrl="http://127.0.0.1:5100"    # where the website host (BFF) reaches the Api
export Api__ServiceToken="<long random string>"   # BFF-only host credential, not user permission; Web and Api must share the same value
export Backup__Enabled=true   # optional, defaults to false; enables the daily automatic backup (Backup:Keep retention, default 7; Backup:HourUtc run hour, default 3)
```

`Storage__RootPath` must be an absolute path outside the published application directory. The default
site keeps `Storage__RootPath/images`; new sites use `Storage__RootPath/sites/{SiteId:N}/images`
(`N` is a GUID without hyphens). Daily backups write `origami-backup-*.zip` (JSON + images) into the
same site's `backups/`: `Storage__RootPath/backups/` for the default site or
`Storage__RootPath/sites/{SiteId:N}/backups/` for a new site, retaining the newest few. Web and Api
must use the same persistent storage root. The Windows DPAPI-protected Data Protection keys remain
global at `Storage__RootPath/keys`; this batch does not move them.

`Api:ServiceToken` authenticates the BFF host, not a user or write permission. Management operations
also require the real user's forwarded identity and security stamp, with role revalidation. Without
a configured service token, BFF authentication is unavailable, but issued, valid PATs can still
authenticate. Supply the service token only to Web and Api through secure external configuration;
never distribute it to browsers, third parties or mobile apps. External API access uses restricted
PATs, not the BFF secret.

#### Site selection and deployment prerequisites

- Sites share the application and PostgreSQL, using independent DNS domains/subdomains. Each keeps
  its existing `/posts`, `/pages` and `/media` paths; site selection is not a URL path prefix.
- Api/MCP select a site through a single GUID in `X-Origami-Site`. Omitting it selects the default
  `11111111-1111-1111-1111-111111111111`. Malformed headers return 400; missing or inactive sites
  return 404. The header selects a site but grants no permission.
- Web resolves the request Host through Api's `/api/v1/sites/resolve?host=…`, not a browser-supplied
  site header. Unregistered Hosts always return 404; the only exception is loopback development
  hosts (`localhost` / `127.0.0.1`), which still resolve to the default site while its `HostName = null`.
  Binding a Host to the default site disables that fallback even for loopback. A registered inactive
  Host always returns 404 and never falls back.
- Registration supports DNS names, IPv4 and `localhost`, not schemes, ports, paths, wildcards or
  IPv6 literals.
- Operations staff configure DNS, TLS and the reverse proxy; this round does not operate or deploy
  them. Plan routing for registered Hosts, HTTPS and trusted proxy boundaries before deployment.
  The HTTP loopback examples below are not a complete production setup: browser secure cookies need HTTPS.
  `/api/v1/admin/users` requires HTTPS, except for an authenticated BFF connecting over same-machine
  loopback. This HTTP exception does not apply to a BFF on another machine.

### 2. Apply the schema

First make complete backups of PostgreSQL, every site's media and the global keys.
`AddSiteIsolationAndApiCredentials` has been generated but has not been applied to the production
database; commands here are instructions, not a record of execution. The migration is designed to
retain existing content, Settings, media records and URLs under the default site, and preserve the
existing administrator as network admin without moving old media paths. Once sites, accounts,
memberships or credentials are in use, `Down` refuses a downgrade to prevent data and permission
loss. Plan rollback separately; do not force a downgrade instead of restoring backups. JSON/ZIP
exports omit accounts and PATs and cannot replace full database, media and key backups.

```bash
dotnet run --project src/Origami.Web -- migrate
```

The schema is managed by EF Core migrations. Upgrades never drop and recreate the database.

### 3. Bootstrap the unique network admin

```bash
dotnet run --project src/Origami.Web -- admin-init
```

The password is read from an interactive console without echo. Credentials are never accepted as
command arguments or through redirected input, and there is no public registration or
initialization endpoint. `admin-init` creates only the unique network admin, not ordinary users.
The network admin creates ordinary accounts in the admin area; site administrators manage their
site's memberships. `admin-reset` recovers the network admin account on the server and rotates its
security stamp, immediately invalidating existing sessions and old PATs.

### 4. Run

```bash
# The Api host also serves the website's page data; it must be up before the website starts.
ASPNETCORE_URLS=http://127.0.0.1:5100 dotnet run --project src/Origami.Api

# The website host is a pure BFF client: sign-in, two-factor and media files stay local,
# every data read and write goes through the Api.
ASPNETCORE_URLS=http://127.0.0.1:5000 dotnet run --project src/Origami.Web
```

The public site is at `/`, the admin area at `/admin/posts`. Uploaded images are still served
by the website at `/media`; migrations and admin commands still run only through the Web host.

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
src/Origami.Api/              # Machine-facing host: public JSON, BFF/PAT admin API /api/v1, read-only MCP /mcp
mobile/                       # Mobile reader client (Avalonia 12, separate solution), see mobile/README.md
tests/Origami.Tests/
```

## Data model

| Model | Responsibility | Database constraints |
| --- | --- | --- |
| `AdminUser` (Identity) | Global account, password hash, security stamp, disabled state | `SingletonKey` is nullable; a non-null value is unique and must be `1`, identifying the unique network admin without limiting ordinary accounts |
| `CmsSite` | Site directory, Host mapping, active state | Unique site slug and non-null Host |
| `SiteMembership` | A user's site role | Composite key `(SiteId, UserId)`; Reader/Editor/Administrator |
| `ApiCredential` | User-and-site-bound PAT hash, scope, stamp, expiration and revocation | No plaintext secret; user, site and current permissions are revalidated |
| `Content` | Articles and pages (`Kind`), title, slug, Markdown body, excerpt, cover reference, status, timestamps, version | `(SiteId, Slug)` unique within a site; check constraints on status, title, slug and publish time; `Version` is the concurrency token; private and password-protected cannot be combined; a page cannot point at itself |
| `Category` / `Tag` | Article categories and tags with descriptions | Name and slug uniqueness checks are scoped to the same site and kind |
| `ContentCategories` / `ContentTags` | Many-to-many links | Same-site relationship constraints; removed together with the content |
| `MediaAsset` | File identity, media type, size, dimensions, alt text, upload time | `(SiteId, FileName)` unique; image bytes live in the site's disk directory |
| `SiteSettings` | Per-site name and description, articles per page, feed items, fixed home page, site icon | Key `(SiteId, Id)` with `Id = 1` per site; home page and icon are released with their content |
| `ContentSlugRedirect` | Retired slug to live content for permanent redirects | Old slug unique within a site; removed with its content |
| `ContentRevision` | Previous-state snapshot taken on every save | Removed with its content; newest 20 kept |
| `ContentView` | One day of view counts per content | Composite key (content, date); removed with its content |

Content also carries a parent page (`ParentId`, pages only), the draft preview token, the scheduled
publish time `PublishAt` and the trash timestamp `DeletedAt`; password-protected content stores an
Identity password hash, and private content is indistinguishable from a missing page for visitors.

Status is `Draft`, `Published` or `Trashed`; withdrawing returns content to `Draft`, and a delete
moves it to the trash for 30 days with restore and an automatic cleanup. The first publish time is
written once and preserved across later edits, withdrawals, re-publishes and scheduled publishing.

## Authentication and authorization

- Web uses ASP.NET Core Identity with cookies: `__Host-` prefix, `Secure`, `HttpOnly`,
  `SameSite=Lax` and host-only (no cross-domain Domain attribute). Each site requires its own
  sign-in; there is no cross-site SSO.
- There is no public registration or initialization endpoint. The CLI bootstraps/recovers only
  the unique network admin; ordinary accounts are created by the network admin in the admin area.
  Two-factor setup and removal affect only one's own account.
- Api registers IdentityCore/EF stores for controlled account management, not cookies,
  SignInManager, background workers or static media hosting. Browser sign-in stays in Web.
- Every protected operation re-checks the current user, security stamp, enabled state, site role
  and PAT status/scope against a fresh `DbContext`. Submitted identities or roles are not trusted;
  the stored row remains authoritative for the content kind on update.
- Sign-out and password recovery rotate the security stamp, immediately invalidating existing
  interactive sessions and old PATs. Role changes, membership removal and account/site disabling
  take effect on subsequent operations without waiting for periodic session validation.
- `Reader` has read-only management access, including drafts, not merely public posts. `Editor`
  can write content, media and taxonomies. `Administrator` additionally manages site settings,
  import/export and members. The network admin manages the site directory and global accounts
  through the website and has cross-site administrative access. Author ownership and editorial
  review workflows are not part of this batch.
- BFF host authentication uses `Authorization: Bearer <Api:ServiceToken>`. Management operations
  also forward `X-Origami-User` and `X-Admin-Security-Stamp` for the real signed-in user; either
  missing means 401, never a synthesized administrator. Insufficient permissions return 403,
  not a sign-out. A trusted BFF can still fetch public presentation data for anonymous visitors.
- PATs use `Authorization: Bearer <PAT>` and are bound to a user and a site. Effective permission
  is the intersection of the current site role and scope ceiling: `Read` is at most `Reader`,
  `Write` at most `Editor`. Even a network admin's PAT cannot manage sites/accounts/members/tokens,
  site settings or import/export, or call BFF-only presentation, unlock/unlock-state or view-count
  endpoints. Forged forwarded-identity headers cannot elevate a PAT.
- The fixed PAT format is `org_<32-lowercase-GUID-digits>.<64-lowercase-hex-secret>`
  (`org_GUID.secret`). The full secret is returned only once after successful creation; the
  database stores only its hash. Each user/site can have at most 20 valid tokens, expiring in the
  future within 365 days. Management lists prioritize valid tokens, followed by history, up to
  100 records in total; historical records are not deleted.
- Revocation, expiry, security-stamp changes, disabled accounts or removed membership stop the
  corresponding PAT access immediately; every operation revalidates it. Valid PAT traffic is
  limited to 120 requests/minute/token and 600/minute/IP simultaneously. Only trusted BFF traffic
  is exempt from machine-interface anonymous limits; PATs never inherit that exemption. Anonymous
  public machine requests remain limited to 30/minute/IP, and unauthenticated management requests
  to 600/minute/IP, including failed authentication.
- Five failed sign-ins lock the account for 15 minutes. Repeated failures beyond 20 per
  account-and-client combination receive `429`; a successful sign-in resets the counter.
- The two-factor stage obeys the same account lockout: a locked account cannot sign in even with a
  correct code, and the pending cookie is bound to the security stamp, so a password reset or a
  two-factor change ends the pending window on the server.
- Private content is not public; website presentation requires at least the current site's Editor
  role, otherwise it returns `404`. Management reads follow the role rules above. Private or
  protected content never enters keyword search, semantic retrieval or the MCP tools.
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
| `/account/login` | User sign-in (Static SSR, host-only cookies; management access follows the current site's role) |

Machine interface (the dedicated `Origami.Api` host, `no-store` responses): public JSON and MCP
remain read-only, with anonymous public requests limited to 30/minute/IP. Management APIs support
BFF and restricted PAT access under the roles and limits above. A PAT does not make public article
endpoints expose drafts; use authorized management reads for drafts.

| Route | Content |
| --- | --- |
| `/api/v1/posts` | Published article list: pagination (`page`, `pageSize`) plus `category`/`tag`/`year`/`month` filters |
| `/api/v1/posts/{slug}`, `/api/v1/pages/{slug}` | Article and page detail (raw Markdown); drafts, protected and private content always return `404` |
| `/api/v1/search?q=` | Hybrid search with the same rules as `/search`; keyword-only when no model is configured |
| `/mcp`, `/health` | Read-only MCP tools (`search_posts`, `get_post`, `get_page`) and database liveness |
| Public reads | `/settings`, `/navigation`, `/contents/{id}/neighbors`, `/contents/{id}/related`, `/archives`, `/categories/popular`, `/taxonomies/{kind}/{slug}`, `/redirects/{slug}`, `/preview/{token}`, `/sitemap`, `/feed`, `/media/widths` — the data source behind every website page |
| `/api/v1/admin/…` | Management reads/writes (content, taxonomies, media, settings, revisions, preview links, export/import); BFF forwards the real user, while PATs access content operations under Read/Write and role ceilings, not settings or import/export |
| `/api/v1/sites/resolve?host=…` | Public Host mapping; returns only site metadata, not accounts or credentials |
| `/api/v1/admin/access`, `/sites`, `/users`, `/members`, `/credentials` | All but the first path in this row are relative to `/api/v1/admin`; BFF-only access, site, global account, membership and PAT management, with service-level role checks |
| BFF-only presentation | Published content/hierarchical page presentation, unlock/unlock-state and view counting remain exclusive to the trusted website host, not PATs |

Admin area (Interactive Server, per page render mode):

| Route | Content |
| --- | --- |
| `/admin/posts`, `/admin/pages` | Content list with keyword search, status filters and the trash; quick edit (title/slug/status in place); publish, withdraw, confirmed delete |
| `/admin/content/{id}` | Markdown editor, preview, taxonomy assignment, media insertion, scheduled publishing, revision history, protection and privacy settings |
| `/admin/taxonomies` | Create, edit and delete categories and tags |
| `/admin/media` | Image upload, media library search, alt text, reference-protected deletion |
| `/admin/settings` | Site name and description, articles per page, feed items, fixed home page, site icon, data export (JSON / ZIP full backup) with replace-style import |
| `/admin/security` | Two-factor (TOTP) setup and removal for one's own account |
| `/admin/sites` | First batch: accessible site list/switching; network admin creates and updates sites, Host mappings and active state |
| `/admin/users` | First batch: network admin creates and enables/disables global ordinary accounts |
| `/admin/members` | First batch: Administrator/network admin manages the current site's members and roles |
| `/admin/credentials` | First batch: current user's PAT list, creation and revocation for the current site; secret displayed only once |

New management routes await verification after Web integration, not browser acceptance sign-off.
Content reads allow Reader; content/media/taxonomy writes require Editor or above, and settings,
import/export and members require Administrator or above. Hiding buttons is not authorization.
Switching sites opens the target site's registered Host and requires sign-in on that Host.

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

**First-batch verification is limited to compilation; the user's final batch regression is
pending.** This documentation update ran no commands, tests, deployments or migrations. The table
and commands below describe existing coverage/historical acceptance baselines, not a full test
report for this batch. Historical accepted results and counts retain their original scope and do
not apply to this batch. Older single-admin/service-token-only descriptions in the table document
the original baseline; the new contracts require separate regression verification.

```bash
dotnet test tests/Origami.Tests/Origami.Tests.csproj -c Release
```

Tests run against a PostgreSQL cluster they create themselves (SCRAM authentication, random port)
and never touch an existing database. When the server binaries are unavailable the PostgreSQL
tests are reported as *skipped* with a diagnostic instead of silently passing on another provider.
`RunningSite` now boots an Api host alongside the website (same database, same service token), so
the web tests exercise the full BFF chain.

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
| `CoverImageTests` | Cover assignment and clearing, public list/detail with cover, caption limits, cover-only references block deletion, admin API round trip, front-end og:image and srcset rendering |
| `BulkOperationsTests` | Bulk trash/restore/delete with per-item results, conflicts and missing items never break the batch, empty and oversized batches rejected, anonymous rejected, admin API endpoint validation |
| `QuickEditTests` | Quick edit changes title/slug/status, retired slug gets a 301, publishing clears the schedule and syncs embedding lifecycle, conflict/trash/invalid input rejected, BFF endpoint validation |
| `ArchiveTransferTests` | ZIP archive packs data and images, restore writes files and trims orphans, missing media rejected before any data change, JSON and ZIP analysis, export endpoint |
| `BackupServiceTests` | Daily backup lands as a valid archive, retention limit trims the oldest, no staging files remain |
| `AdminEfficiencyBrowserTests` | Browser flows for quick edit, picker search and the excerpt cut point |
| `SyndicationEmbedTests` | Category-filtered feeds and 404s, Atom 1.0 output and filtering, subscription link on filtered lists, oEmbed for public content only, format and path validation, detail-page discovery link |
| `PageHierarchyTests` | TextDiff line results and oversize fallback, page path resolution with flat fallback for unpublished ancestors, sitemap hierarchy paths, flat-to-hierarchy 301, wrong middle segment 404, three-segment oEmbed |
| `PresentationPolishTests` | Remember-me persistent cookie switch, full-text feeds (content:encoded / Atom content) with author element, byline rendering, paginated search with totals, out-of-range page 404, apple-touch-icon link and 404 |
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

The 2026-10-28 audit-fix round added and individually passed: migration upgrade of a legacy single-site
database into the default site (`DatabaseIntegrationTests`), PAT rate-limit partitioning with the BFF
exemption and per-credential isolation (`TenancyApiTests`), BFF happy paths for site, account and member
management, browser flows for the four new admin pages (`AdminTenancyBrowserTests`), per-site backup
directory isolation (`BackupServiceTests`), and the Web host startup fix for the Development environment.
The remaining first-batch boundaries (cross-Host isolation, the migration Down guard, Reader/role write
boundaries, BFF 401/403, PAT immediate invalidation and token caps, own-account 2FA) still await the
user's full batch regression; the batch is not claimed accepted before that passes.

## Known limitations

- Multi-site, multi-user and role support are under first-batch implementation/acceptance, still
  limited to one application instance and a fixed public layout, not distributed/multi-instance
  deployment. Use registered independent DNS domains/subdomains (IPv4/localhost for local use,
  no IPv6 literal registration); DNS/TLS/reverse proxy configuration remains an operations task.
- All uploaded images are public, including those referenced only by drafts. Site directory
  isolation is not private-attachment authorization.
- The Data Protection key ring is encrypted with Windows DPAPI, so it can only be decrypted on the
  same machine by the same user. Recovery on another machine requires re-provisioning. Keys remain
  global and are not moved by this batch.
- A data export (JSON or ZIP) contains the password hashes of protected content, which can be attacked offline.
  Treat the file as a secret and keep it out of version control and shared drives. An import
  replaces every piece of text data on the current site in one transaction. A JSON import leaves image
  files untouched; a ZIP import also restores that site's images and trims images outside the manifest
  (replace semantics), with a 256 MB media cap. "Full backup" here means that site's content-plus-images
  archive only: no accounts, memberships, PATs or global keys. It cannot replace full PostgreSQL,
  media and key backups.
- Public API and MCP reads remain anonymous and read-only. Third parties/apps can use user-and-site-bound
  PATs for authorized management content access, with writes capped at Editor. PATs cannot manage
  sites, accounts, settings or import/export, or call BFF-only presentation/unlock routes. Backend
  support does not mean a mobile writing UI has been implemented.
- The website and the Api are two processes: the Api must be reachable before the website starts
  (`Api:BaseUrl`) and both must share the same `Api:ServiceToken`; adjacent ports on one machine
  are recommended for single-instance deployments. Configure HTTPS; HTTP loopback examples are
  not a production account-management setup.
- Sign-in, two-factor, the site assistant, background workers (embedding, trash cleanup), database
  migrations and admin commands still run inside the website host.
- No comments, plugins, menu editor, theming, author ownership, editorial review, SSO or private
  attachments. No PHP runtime or WordPress table/REST API/plugin/theme compatibility layer.
- `AddSiteIsolationAndApiCredentials` has not been applied to production. Once sites/accounts/
  memberships/credentials are used, `Down` refuses a downgrade; plan full backups and recovery
  separately before deployment or rollback.

## License

MIT — see [LICENSE](LICENSE).
