# Origami

A content management system for publishing articles and standalone pages, written in
**C# / ASP.NET Core Blazor** on **PostgreSQL**. It re-implements the core publishing workflow of
WordPress 7.1.3 in a modern .NET stack — no PHP, no `wp_*` table compatibility, no arbitrary-code plugin system.

> **Scope:** multiple sites and users sharing one application and PostgreSQL database, a single
> application instance and controlled public presentation. The first batch (2026-10-28) adds site isolation,
> roles and personal API tokens (PATs), and is still under implementation/acceptance. This batch
> is limited to compilation verification; the user's batch regression run is pending. This is not
> a claim of full test success, deployment or a production database migration. Existing features
> and historical acceptance baselines are retained, not reclassified as passing this batch.
> The product scope and security baseline are defined in [`agent.md`](.qoder/rules/agent.md); this document
> describes how to run and verify the implementation.
>
> [繁體中文說明](README.zh-TW.md)

> **Product completion round (from 2026-11-05):** editor enhancements, declarative content
> modeling, admin efficiency, navigation and widgets, comment moderation, account mail,
> multilingual UI and engineering improvements are implemented, together with Markdown/Medium
> portable import and off-site WebDAV backup uploads. Focused verification is in progress and the
> user's final batch regression has not run; this is not a claim of full acceptance, deployment or
> production-data migration.

**Languages:** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md)

## Features

| Area | What it does |
| --- | --- |
| Administrators & users | CLI bootstrap of the unique network admin, ordinary accounts and site memberships managed in the admin area, cookie sign-in with "remember me", sign-out, server-side password recovery, TOTP for one's own account |
| Sites & roles (first batch) | Independent domains/subdomains sharing the application and PostgreSQL; site-isolated content, settings, relations, media, search indexes and background work; Reader/Author/Editor/Administrator site roles and network admin |
| API credentials (first batch) | User-and-site-bound Read/Write PATs, expiration and revocation, a secret returned only once on creation; BFF and PAT authorization and rate limits are separate |
| Articles | List, create, edit, draft, publish, withdraw, sticky, scheduled publishing, excerpt, cover image, confirmed permanent delete, trash with restore, list bulk actions (trash/restore/delete many), list quick edit (title/slug/status in place), translation groups (peer-to-peer translations with language tags, ?lang= switching with fallback notice, per-group dedup in lists) |
| Pages | Standalone URLs, hierarchical navigation with hierarchical URLs (/pages/parent/child/, flat URLs redirect 301), cycle protection, usable as the fixed home page |
| Categories & tags | Hierarchical categories (cycles rejected; children promoted when a parent is deleted) and flat tags, many-to-many assignment, descriptions, deletion never removes articles, merge (reassign relations to a target, then remove the source), default categories/tags, tag-to-category conversion, and dedicated `/category/{slug}` and `/tag/{slug}` archives |
| Media | Image and file upload (images re-encoded; PDF／audio／video／XLSX／CSV stored verbatim after magic-number and content validation), import from URL (sideload: public absolute https URLs only, local/intranet/reserved addresses rejected, redirects not followed, timeout and per-file caps — downloaded content goes through the same format validation), media library, insert as Markdown, alt text and caption, cover image assignment (posts and pages), reference-protected deletion, responsive variants with srcset, keyword search in the editor picker, automatic EXIF orientation correction and WebP variants |
| Public site | Article list with pagination and cover thumbnails, article and page detail with cover, caption and author byline, neighbour and related posts, category/tag/monthly archives, hybrid search (category/date filters, pagination and keyword highlighting), nested comments, basic navigation, mobile-friendly layout and a front-end admin toolbar for signed-in members |
| Openness | RSS 2.0 and Atom 1.0 feeds (filterable by category/tag, optional full text, per-post comment feeds), oEmbed provider (JSON + discovery links), OpenSearch description document (registers site search as a browser engine; disabled together with the index switch), sitemap.xml, robots.txt, article-level Open Graph/Twitter Card (og:image prefers the cover, falls back to the first inline image), canonical and translation hreflang metadata, 301 redirects for retired slugs, JSON/WXR export, JSON/ZIP replace-style import, additive WordPress WXR and Markdown/Medium portable import, ZIP full backup (JSON + media (`media/` and `files/`)) with restore, opt-in daily backup with off-site WebDAV upload, `/health` database liveness, public read-only JSON API `/api/v1` and the MCP endpoint on a dedicated Api host, alongside restricted management APIs, response compression and security headers |
| Editor safety net | Autosave every 60 s for existing drafts, revision history (20 per item with line-diff against the editor and A/B diff between any two revisions), trash with 30-day retention and restore, revocable draft preview links, code syntax highlighting, toolbar/shortcuts/find-replace/fullscreen, Word and Google Docs HTML paste converted to Markdown, word count and reading-time estimates, AI writing assist sidebar (outline / draft / rewrite selection / continue / title ideas — preview-confirmed inserts only, manual save still required) |
| Visibility | Private content is not public and requires management authorization, password-protected content needs the password, and neither enters search or AI retrieval |
| Site settings | Site name and description, articles per page, feed items with a full-text option, author display name, fixed home page, site icon, default categories/tags, default interface language, 404 copy, logo, data export (JSON / WXR / ZIP), JSON/ZIP replace-style import and additive WordPress WXR import and additive Markdown/Medium portable import that creates drafts and never downloads external images |
| Appearance & themes | Controlled theme presets (reading serif / minimal compact / gallery display — real differences in fonts and layout density), accent color and column width fine-tuning, live preview on the settings page, automatic foreground lightening in dark mode, a per-admin three-state appearance toggle remembered via cookie (takes precedence over system detection when the site is set to auto), prefers-contrast support |
| Widgets & custom CSS | Sidebar and footer widgets: fifteen built-in declarative types (recent posts / categories / tags / search box / Markdown text / recent comments / external RSS/Atom feed / tag cloud / calendar / page list / monthly archives / image / navigation menu / social links / audio-video), sortable and hideable, sidebar beside content on wide screens and stacked on narrow ones. The tag cloud weights font size by popularity and the calendar shows per-day publish counts for the current month, linking to the date archives. Custom CSS is sanitized before saving (comments stripped; @import, @font-face, expression, javascript: and backslash escapes are rejected) and sanitized again before output |
| Controlled package market | Theme packs and widget packs: the built-in catalog ships five visually distinct themes (inkwell literary classic, headline newspaper editorial, terminal monospace geek, atelier photography gallery, zen swiss minimal), and each catalog card renders the theme's real layout. Sources are the built-in catalog and backend ZIP import only, with a full install / enable / disable / uninstall lifecycle. A theme pack is a layout declaration (accent color, column width, font stack key, line height / density / heading scale, optional controlled CSS); a widget pack is a ready-made combination of the built-in widget types. No code is ever loaded, font stacks accept allow-list keys only, and pack CSS follows the same cleaning rules as custom CSS. Packages travel with site data export and import, can be exported as a shareable ZIP for other sites, and third-party (WordPress) theme archives can be converted into controlled theme packs — only the style.css header metadata plus a derivable accent color and font family are read; templates and code are never kept or executed |
| Dashboard & audit | Admin overview (published articles/pages, comment moderation, 30-day view trends and popular content) plus a permanent audit log of publishing, editing, settings and security operations |
| Content types & navigation | Custom content types (CPTs) with their own taxonomies and fields, the site navigation editor, an author hub (`/author/{slug}`) and author-specific content |
| Comments & anti-spam | Visitor comments (pending / approved / spam / trash), sensitive-word and honeypot anti-spam, official administrator replies, new-comment mail notifications (Author and above, plus network admins), and reply subscriptions with one-click unsubscribe links (GET confirmation, then POST); visitors can submit comments only through the BFF |
| Notifications & health | Outbound webhooks (`content.published` / `comment.created`, HMAC-SHA256 signatures, exponential-backoff retry and manual replay) and the SiteHealth panel (database, storage, scheduling, embeddings, webhooks, SMTP, backups and configuration) |
| Site operations | Maintenance mode (HTTP 503, signed-in members exempt), dark/light/automatic color modes, site logo, 301 redirect management and two-factor recovery codes |
| Profile & privacy | Local avatar from the media library (preferred over an external URL and shown on author archives), per-user interface language, account data export and erasure (GDPR); network admins can act for another account, anonymizing that account's content and comments together |
| Editing assistance | Editor AI summary and SEO-description suggestions (deterministic local fallback when no model is configured), plus the AI writing sidebar (preview-confirmed inserts only, manual save still required, Author and above) |
| Interface languages | Traditional Chinese, English and Simplified Chinese, with a site default and a per-user preference; switch through `?lang=` or a cookie. UI text lives in resource files, and tests reject missing translations or mismatched format placeholders |
| Multilingual content | Peer-to-peer translation groups (members are equivalent translations; one content item per language per group), BCP 47 content language tags, `?lang=` translation switching without a path prefix, fallback to the default-language version with a notice, and language-aware deduplication in public lists; links translations only and never translates automatically |
| Structured content | Articles, pages and custom content types can carry custom fields (text, long text, number, date, checkbox, URL, select, media, email, date-time, gallery and related content); reusable content templates and text snippets are available in the editor |
| Admin efficiency | Sortable content-list columns, page-size selection, custom-type filters, bulk status/category/tag/sticky edits and a scheduled-post overview; front-end admin toolbar, command palette and global search, plus a first-run setup checklist |
| Comments & accounts | Nested comment replies, editing and bulk moderation, remembered visitor identity, per-post comment feeds and reply subscriptions; email verification, welcome mail, password-change notices, test mail and mail log, sign-in history, and content transfer when deleting an account |
| Navigation & presentation | Multi-level menus, multiple menu locations and category/tag/archive menu items; fifteen widget types, site logo, controlled themes and layout parameters, and sanitized custom CSS |

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
export Backup__WebDav__Url="https://dav.example.test/backups"   # optional; local backup only when omitted
export Backup__WebDav__Username="<WebDAV username>"
export Backup__WebDav__Password="<WebDAV password>"   # secure external configuration only; never logged or stored in the backup
export Backup__WebDav__Directory="origami"   # optional; the remote path is further isolated by site-{SiteId:N}
export Smtp__Host="smtp.example.test"
export Smtp__Port="587"
export Smtp__User="origami@example.test"
export Smtp__Password="<SMTP password>"
export Smtp__From="origami@example.test"
export Smtp__EnableSsl="true"
export Ai__Embedding__Endpoint="https://models.example.test/v1"   # optional; semantic search falls back to keywords when omitted
export Ai__Embedding__Model="text-embedding-3-small"
export Ai__Embedding__ApiKey="<embedding model key>"
export Ai__Assistant__Endpoint="https://models.example.test/v1"   # optional; assistant and AI writing prompts are unavailable when omitted
export Ai__Assistant__Model="gpt-4.1-mini"
export Ai__Assistant__ApiKey="<assistant model key>"
export Authentication__External__Google__ClientId="<Google OAuth Client ID>"   # optional
export Authentication__External__Google__ClientSecret="<Google OAuth Client Secret>"
export Authentication__External__Microsoft__ClientId="<Microsoft OAuth Client ID>"   # optional
export Authentication__External__Microsoft__ClientSecret="<Microsoft OAuth Client Secret>"
```

`Storage__RootPath` must be an absolute path outside the published application directory. The default
site keeps `Storage__RootPath/images`; new sites use `Storage__RootPath/sites/{SiteId:N}/images`
(`N` is a GUID without hyphens). Daily backups write `origami-backup-*.zip` (JSON + media (`media/` and `files/`)) into the
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

When WebDAV is configured, each automatic or manual backup first completes the local archive, then
uploads it with HTTPS Basic Authentication to `<Url>/<Directory>/site-{SiteId:N}/`. Credentials in
the URL, query strings and fragments are rejected, and redirects are never followed. Remote cleanup
only touches timestamped `origami-backup-*.zip` files inside that site's directory and retains the
newest `Backup:Keep` copies. A remote cleanup failure never undoes the completed local backup.

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
`create-user` also creates only a global account and grants no site membership. The network admin
can create ordinary accounts in the admin area; site administrators manage their site's memberships.
`admin-reset` recovers the network admin account on the server and rotates its security stamp,
immediately invalidating existing sessions and old PATs.

These commands operate on the **default site only** and require an interactive terminal with a fresh
network-admin authentication (hidden password, plus a TOTP or one-time recovery code when two-factor
is enabled). Redirected input and command-line credentials are rejected. `export` accepts JSON or ZIP
(default ZIP), `import` requires the exact `REPLACE` confirmation, and `create-user` creates an ordinary
account without assigning it to a site:

```bash
dotnet run --project src/Origami.Web -- export
dotnet run --project src/Origami.Web -- import
dotnet run --project src/Origami.Web -- create-user
```

### 4. Run

```bash
# The Api host also serves the website's page data; it must be up before the website starts.
ASPNETCORE_URLS=http://127.0.0.1:5100 dotnet run --project src/Origami.Api

# The website host is a pure BFF client: sign-in, two-factor and media files stay local,
# every data read and write goes through the Api.
ASPNETCORE_URLS=http://127.0.0.1:5000 dotnet run --project src/Origami.Web
```

Run a backup for every active site at any time (local archive plus configured WebDAV upload):

```bash
dotnet run --project src/Origami.Web -- backup
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
| `SiteMembership` | A user's site role | Composite key `(SiteId, UserId)`; Reader/Author/Editor/Administrator |
| `ApiCredential` | User-and-site-bound PAT hash, scope, stamp, expiration and revocation | No plaintext secret; user, site and current permissions are revalidated |
| `Content` | Articles and pages (`Kind`), title, slug, Markdown body, excerpt, cover reference, status, timestamps, version | `(SiteId, Slug)` unique within a site; check constraints on status, title, slug and publish time; `Version` is the concurrency token; private and password-protected cannot be combined; a page cannot point at itself |
| `Category` / `Tag` | Article categories and tags with descriptions | Name and slug uniqueness checks are scoped to the same site and kind |
| `ContentCategories` / `ContentTags` | Many-to-many links | Same-site relationship constraints; removed together with the content |
| `MediaAsset` | File identity, media type, size, dimensions, alt text, upload time | `(SiteId, FileName)` unique; image bytes live in the site's disk directory |
| `SiteSettings` | Per-site name and description, articles per page, feed items, fixed home page, site icon | Key `(SiteId, Id)` with `Id = 1` per site; home page and icon are released with their content |
| `ContentSlugRedirect` | Retired slug to live content for permanent redirects | Old slug unique within a site; removed with its content |
| `ContentRevision` | Previous-state snapshot taken on every save | Removed with its content; newest 20 kept |
| `ContentView` | One day of view counts per content | Composite key (content, date); removed with its content |
| `ContentType` / `CustomField` / `ContentFieldValue` | Custom content types, declarative field definitions and values | Site-scoped; fields attach to built-in posts/pages or custom types, and values are validated by field kind |
| `ContentTemplate` / `ContentSnippet` | New-content skeletons and reusable text fragments | Site-scoped; applying or inserting never rewrites existing content |
| `SiteNavItem` / `SiteWidget` | Multi-level navigation and fifteen built-in declarative widget types | Site-scoped; explicit navigation overrides automatic page navigation when enabled |
| `SitePackage` / `WebhookDeliveryRecord` / `AuditLog` | Controlled theme/widget packs, webhook delivery retries and management audit events | Site-scoped; only declarative packages and necessary audit summaries are stored |
| `ContentComment` | Visitor comments, parent links, moderation state, official replies and unsubscribe tokens | Site-scoped relations; the service validates hierarchy and uses a version for concurrent moderation |
| `EmailLog` | Mail results: recipient, subject, success and diagnostic summary | Site-scoped; only necessary summaries are stored, never passwords, tokens or message bodies |
| `LoginEvent` | Successful and failed account sign-ins | Global account records; only truncated source-address and user-agent summaries are stored |

Content also carries a parent page (`ParentId`, pages only), the draft preview token, the scheduled
publish time `PublishAt` and the trash timestamp `DeletedAt`. Peer translation groups use
`TranslationGroupId`, content language uses the BCP 47 `Language` tag, and editor occupancy stores
`EditingById` / `EditingByName` / `EditingAt`. `ContentRevision.SnapshotJson` keeps the complete
structured snapshot, including SEO, cover, author, taxonomies and custom fields; site settings also
carry the default interface language, 404 copy and comment display depth.
Password-protected content stores an Identity password hash, and private content is indistinguishable
from a missing page for visitors.

Status is `Draft`, `Pending`, `Published` or `Trashed`; Authors submit `Pending`, and Editors approve
publication. Withdrawing returns content to `Draft`, and a delete moves it to the trash for 30 days with
restore and an automatic cleanup. The first publish time is written once and preserved across later
edits, withdrawals, re-publishes and scheduled publishing.

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
- `Reader` has read-only management access, including drafts, not merely public posts. `Author`
  can create content and submit it for review, but cannot publish or edit other people's content.
  `Editor` can write content, media and taxonomies. `Administrator` additionally manages site
  settings, import/export and members. Pending content uses the `Pending` status and requires an
  Editor or above to approve publication. The network admin manages the site directory and global
  accounts through the website and has cross-site administrative access.
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
| `/posts/{slug}` | Article detail; unpublished or unknown slugs return `404`; includes neighbour and related articles. Compatibility routes also accept `/posts/{year}/{month}/{slug}`, `/posts/{year}/{month}/{day}/{slug}` and `/archives/p/{id}` |
| `/pages/{*SlugPath}` | Page detail with hierarchical URLs such as `/pages/parent/child/`; unpublished pages return `404`, and retired flat URLs redirect 301 to the current hierarchical URL |
| `/archives`, `/archives/{year}/{month}`, `/category/{slug}`, `/tag/{slug}`, `/author/{slug}` | Monthly, category, tag and author archives of published content |
| `/t/{Slug}`, `/t/{Slug}/{TaxonomySlug}/{TermSlug}`, `/t/{Slug}/{ContentSlug}` | Custom-type overview, custom-taxonomy filtering and custom content detail |
| `/search` | Hybrid keyword/pgvector search with keyword fallback, category/date filters, pagination and keyword highlighting |
| `/feed`, `/feed/atom`, `/feed/comments?post={slug}`, `/sitemap.xml`, `/robots.txt`, `/opensearch.xml`, `/oembed`, `/health`, `/favicon.ico` | RSS/Atom/per-post comment feeds, sitemap, crawler rules, OpenSearch description, oEmbed, database liveness and site icon; published content only |
| `/preview/{token}` | Draft share preview (noindex; revoked or published links stop working, published content redirects) |
| `/account/login`, `/account/verify-email`, `/account/confirm-email-change`, `/account/forgot-password`, `/account/reset-password`, `/account/external-logins` | Sign-in, email verification and email-change confirmation, web password recovery/reset and external-account binding; management access follows the current site's role |

Machine interface (the dedicated `Origami.Api` host, `no-store` responses): public JSON and MCP
remain read-only, with anonymous public requests limited to 30/minute/IP. Management APIs support
BFF and restricted PAT access under the roles and limits above. A PAT does not make public article
endpoints expose drafts; use authorized management reads for drafts.

| Route | Content |
| --- | --- |
| `/api/v1/posts` | Published article list: pagination (`page`, `pageSize`) plus `category`/`tag`/`author`/`year`/`month`/`lang` filters |
| `/api/v1/posts/{slug}`, `/api/v1/pages/{slug}` | Article and page detail (raw Markdown; `lang` selects a translation); drafts, protected and private content always return `404` |
| `/api/v1/search?q=` | Hybrid search with the same rules as `/search`; keyword-only when no model is configured |
| `/api/v1/openapi.json`, `/openapi/v1.json`, `/scalar` | OpenAPI 3.1 contract and Scalar API reference; reference assets are embedded in the Api host |
| `/mcp`, `/health` | Read-only MCP tools (`search_posts`, `get_post`, `get_page`) and database liveness |
| Public reads (relative to `/api/v1`) | `/settings`, `/navigation`, `/widgets`, `/contents/{id}/published`, `/media/widths`, `/contents/{id}/neighbors`, `/contents/{id}/related`, `/archives`, `/categories/popular`, `/taxonomies/{kind}/{slug}`, `/redirects/{slug}`, `/preview/{token}`, `/sitemap`, `/feed`, `/feed/comments` — the data source behind every website page |
| `/api/v1/layout` | Settings, navigation and visible widgets in one layout-shell response |
| `/api/v1/contents/{id}/comments` | Approved public comments for a content item, with `page` pagination |
| Public custom content types (relative to `/api/v1`) | `/types`, `/types/{slug}/contents`, `/types/{slug}/taxonomies/{taxonomySlug}/terms/{termSlug}`, `/types/{slug}/contents/{contentSlug}` |
| `/api/v1/posts/by-id/{id}` | Published article lookup by content ID |
| `/api/v1/authors/{slug}` | Public author details by author slug |
| `/api/v1/admin/…` | Management reads/writes (content, taxonomies, media, settings, revisions, preview links, export/import); BFF forwards the real user, while PATs access content operations under Read/Write and role ceilings, not settings or import/export |
| `/api/v1/admin/transfer/import-portable/analyze`, `/api/v1/admin/transfer/import-portable/apply` | Markdown/Medium portable-import preview and apply; analysis never writes, apply is additive, BFF administrators only |
| `/api/v1/sites/resolve?host=…` | Public Host mapping; returns only site metadata, not accounts or credentials |
| `/api/v1/admin/access`, `/sites`, `/users`, `/members`, `/credentials` | All but the first path in this row are relative to `/api/v1/admin`; BFF-only access, site, global account, membership and PAT management, with service-level role checks |
| BFF-only presentation | Published content/hierarchical page presentation, unlock/unlock-state and view counting remain exclusive to the trusted website host, not PATs |

Admin area (Interactive Server, per page render mode):

| Route | Content |
| --- | --- |
| `/admin`, `/admin/dashboard` | Admin overview with content/moderation counts, view trends, popular content and the first-run setup checklist |
| `/admin/posts`, `/admin/pages` | Content list with keyword/status/type filters, scheduled overview and trash; sortable columns, page size, bulk status/category/tag/sticky edits, quick edit, publish, withdraw and confirmed delete |
| `/admin/content/{id}` | Markdown editor (toolbar, shortcuts, find-replace, fullscreen, syntax highlighting and HTML paste conversion), preview, taxonomy/custom fields, media/gallery/related content, templates and snippets, scheduled publishing, revision comparison, protection and privacy |
| `/admin/types` | Custom content types, custom taxonomies and field definitions |
| `/admin/taxonomies` | Create, edit and delete categories/tags, set defaults, bulk delete and convert tags to categories |
| `/admin/navigation`, `/admin/widgets` | Multi-level menus and multiple locations; create, edit, reorder, show/hide and delete sidebar/footer widgets (fifteen built-in types) |
| `/admin/templates` | Create, edit, reorder and delete content templates and reusable text snippets |
| `/admin/comments` | Nested comment review, editing, official replies, moderation/spam/trash and bulk actions |
| `/admin/marketplace` | Controlled package market: browse the built-in catalog with live theme previews (rendered with the theme's own layout styles), analyze and install uploaded ZIP packages, enable/disable and uninstall (uninstalling a widget pack also removes the widgets it created), export a pack as a shareable ZIP, and convert a third-party (WordPress) theme archive into a controlled theme pack |
| `/admin/media` | Image and file upload (images, PDF, audio, video, XLSX, CSV), import from URL (sideload), media library search with kind filter, alt text, reference-protected deletion |
| `/admin/settings` | Site name and description, articles per page, feed items, fixed home page, site icon/logo, default taxonomies, default interface language, 404 copy, themes and navigation, data export (JSON / WXR / ZIP), JSON/ZIP replace-style import, additive WordPress WXR import and additive Markdown/Medium import |
| `/admin/security` | Own password, TOTP and recovery codes, active sessions and sign-in history, local avatar, interface language preference and external-account bindings |
| `/admin/site-health` | Database/storage/scheduling/embedding/webhook/SMTP/backup/configuration checks, webhook replay, test mail and site-scoped mail logs |
| `/admin/redirects`, `/admin/audit`, `/admin/assistant` | 301 redirect management, management audit events and the read-only site assistant |
| `/admin/sites` | First batch: accessible site list/switching; network admin creates and updates sites, Host mappings and active state |
| `/admin/users` | First batch: network admin creates and enables/disables global ordinary accounts |
| `/admin/members` | First batch: Administrator/network admin manages the current site's members and roles |
| `/admin/credentials` | First batch: current user's PAT list, creation and revocation for the current site; secret displayed only once |

Management routes are wired into Web; browser acceptance still depends on actual regression results.
Reader can read; Author can create and edit their own `Draft`/`Pending` content and submit it for review;
Editor and above can write content, media and taxonomies; Administrator and above manage settings,
import/export and members. Hiding buttons is not authorization. Switching sites opens the target site's
registered Host and requires sign-in on that Host.

## Security notes

- **Images are public.** Every uploaded image is reachable by URL, including images used only in
  drafts. Withdrawing or deleting content does not hide the image. The admin interface states this
  explicitly.
- **Uploads are validated, not trusted.** Raster images accept only PNG, JPEG and WebP; whitelisted
  PDF/audio/video/XLSX/CSV files are also accepted after format validation. The server verifies the
  real format, decodes images, enforces size and dimension limits, and re-encodes images before
  storing. SVG, executables, disguised files and path traversal attempts are rejected, and failed
  uploads leave no readable partial file behind.
- **Markdown is sanitized.** Raw HTML is disabled, a tag and attribute allow-list is applied, and
  only `http`, `https` and local paths are accepted as link targets.
- **Nothing sensitive is committed.** Connection strings, passwords, tokens and Data Protection
  keys are provided through external configuration and are never written to source or logs.

## Testing

**Verification status (2026-11-24):** this round completed a Release build with an isolated
`OutDir` inside the repository and ran focused PostgreSQL/browser tests. The full regression still
awaits the user's batch run. The table below describes existing and new coverage, not a full-pass
report; historical accepted counts retain their original scope and do not apply to this round.
Older single-admin/service-token-only descriptions document the original baseline; the current
contracts still require regression coverage.

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
| `SettingsServiceTests` | Settings persistence, output encoding, length limits, concurrent updates, and one API layout read per request |
| `AdminProvisioningTests` | Single administrator, rejected duplicate and concurrent initialization, password reset that revokes old sessions |
| `ProtectedServiceTests` | Every protected service refuses anonymous or revoked identities |
| `DatabaseIntegrationTests` | Migrations, unique constraints, transactions, UTC storage, database-level rules |
| `MediaServiceTests` | Upload, format spoofing, SVG rejection, size and dimension limits, path traversal, failure cleanup, persistence |
| `MediaFileUploadTests` | Non-image file upload: PDF／audio／video／XLSX／CSV magic-number and content validation, disguise and extension-mismatch rejection, failure cleanup |
| `MarkdownSecurityTests` | Disabled raw HTML, dangerous link and image protocols, XSS, concurrent rendering, editor preview |
| `HttpSecurityTests` | Cookie attributes, antiforgery, lockout and throttling, security headers, public 404s, admin route guards, public-page compression plus immutable media and CSS/JS caching |
| `BrowserFlowTests` | Full publishing loop in a real browser, two-tab conflict, unsaved-change warning, disconnect recovery, mobile layout |
| `PersistenceTests` | Data and images survive a restart, database and file backup/restore, existing sessions keep working |
| `CommandLineTests` / `CliCommandTests` | Migration command, refused credential arguments and redirected input, missing-configuration refusal; CLI export/import round trip, account creation, hidden passwords, wrong authentication and confirmations |
| `StoragePathsTests` | Persistent storage never inside the published directory, relative paths rejected |
| `PublicSyndicationTests` | Manual excerpt precedence and limit, robots/sitemap/feed with published-only content and immediate withdrawal, detail-page og/canonical, feed item cap |
| `PublishingParityTests` | Sticky ordering and page refusal, retired-slug 301 redirects that stop after withdrawal, lazy-loaded images, media alt text, health endpoint, JSON export and its authorization |
| `ContentLifecycleTests` | Trash removal and restore, retention purge, revision cap and restore, scheduled promotion and withdrawal, preview-token lifecycle |
| `MediaClosureTests` | Upload variants and srcset, EXIF orientation correction, WebP variants, reference-protected deletion, variant backfill, custom-field media reference protection, cross-request storage-mutation serialization, and original-file/database-row preservation when deletion fails |
| `CoverImageTests` | Cover assignment and clearing, public list/detail with cover, caption limits, cover-only references block deletion, admin API round trip, front-end og:image and srcset rendering |
| `BulkOperationsTests` | Bulk trash/restore/delete with per-item results, conflicts and missing items never break the batch, empty and oversized batches rejected, anonymous rejected, admin API endpoint validation |
| `QuickEditTests` | Quick edit changes title/slug/status, retired slug gets a 301, publishing clears the schedule and syncs embedding lifecycle, conflict/trash/invalid input rejected, BFF endpoint validation |
| `ArchiveTransferTests` | ZIP archive packs data, images and non-image files, restores media metadata and trims orphans, rejects missing or invalid files before any data change, cancels a locked destination while preserving data and the original file, JSON and ZIP analysis, export endpoint |
| `BackupServiceTests` | Daily backup archive, retention trimming, staging cleanup, manual backup worker and per-site isolation |
| Tests added this round | `MarkdownLayoutTests` / `HtmlToMarkdownTests` (safe layouts and HTML conversion), `EditorToolbarBrowserTests` (editor interaction), `AdminProductivityBrowserTests` (command palette, global search, first-run checklist and site deletion), `ContentShareMetaTests` / `TermArchiveTests` (share/language metadata and taxonomy archives), `TaxonomyDefaultsTests`, `CustomFieldExpansionTests` / `WritingAssetsTests`, `ContentListExpansionTests`, `NavigationWidgetsExpansionTests`, `CommentModerationExpansionTests`, `SearchSettingsExpansionTests`, `AccountMailTests`, `PortableImportTests` / `WebDavBackupTests`, `CliCommandTests` (CLI import/export and user creation) |
| `AdminEfficiencyBrowserTests` | Browser flows for quick edit, picker search, the excerpt cut point, content-list sorting/page size/type filter/scheduled tab, and confirmed site deletion with storage cleanup |
| `SyndicationEmbedTests` | Category-filtered feeds and 404s, Atom 1.0 output and filtering, subscription link on filtered lists, oEmbed for public content only, format and path validation, detail-page discovery link |
| `PageHierarchyTests` | TextDiff line results and oversize fallback, page path resolution with flat fallback for unpublished ancestors, sitemap hierarchy paths, flat-to-hierarchy 301, wrong middle segment 404, three-segment oEmbed |
| `PresentationPolishTests` | Remember-me persistent cookie switch, full-text feeds (content:encoded / Atom content) with author element, byline rendering, paginated search with totals, out-of-range page 404, apple-touch-icon link and 404 |
| `HybridSearchTests` | Keyword hits (CJK and case), semantic merge without duplicates, withdrawal stops hits, public page rendering |
| `SecurityAndProtectionTests` | RFC 6238 vectors and the TOTP flow, admin-reset disabling two-factor, the protection gate with search/MCP exclusion, daily view counts |
| `ProtectionRegressionTests` | A malformed unlock cookie leaks no excerpt, the Static SSR unlock form works, a locked account cannot finish the second factor, private and protected text never reaches the embedding provider |
| `ImportExportRegressionTests` | Import refuses media names outside the image directory, restores the site icon, counts responsive variants as references, and round-trips v3 default taxonomies, covers, content languages and translation groups with new IDs isolated from the source site |
| `LifecycleRegressionTests` | The trash purge keeps restored content, parallel saves never close a page cycle, a scheduled re-publish keeps the first publish time, a fixed home page still reaches the filtered list |
| `EditorRegressionTests` | Creating a preview link never hides another tab's change, a changed schedule counts as an unsaved change |
| `ScheduleTimeTests` | Schedule times convert through the browser time zone across daylight saving, a wall-clock time that does not exist is refused |
| `ApiV1EndpointTests` | /api/v1 list pagination and published-only content, detail `404` for drafts, protected and private items, keyword fallback without a model key, parameter validation and per-IP rate limiting, plus public JSON gzip/no-store/security headers |
| `OpenApiContractTests` / `ScalarApiReferenceTests` | OpenAPI 3.1 route parity, summaries/tags/operationIds and admin Bearer/public anonymous declarations; Scalar page, host-embedded assets and contract link |
| `McpEndpointTests` | The MCP endpoint (Api host) exposes exactly three read-only tools, drafts stay invisible, a missing key degrades at the tool level without a 5xx, per-IP rate limiting |
| `ApiV1AdminEndpointTests` | Write endpoints reject missing or wrong service tokens with 401, the create→publish→withdraw→trash→restore round trip, stale versions are rejected without silent overwrites, admin traffic has its own rate budget |
| `WidgetAndCustomCssTests` | CSS sanitizer rejects dangerous constructs and strips comments, widget CRUD/boundary validation/area-scoped reordering/public projection (published content only), anonymous access refused with per-site isolation, sanitized custom CSS persistence and export/import round trip |
| `MarketplaceTests` | manifest validation boundaries (free-form font stacks, out-of-range colors/widths/density, dangerous CSS, slugs colliding with built-in presets), ZIP container safety (fake format, missing manifest, smuggled entries, path traversal), install/enable/disable/uninstall lifecycle, widget pack materialization and public projection filtering, per-site isolation and role permissions, theme pack effect on the public settings projection, export/import round trip, third-party (WordPress) theme conversion (only the style.css header and derivable visual values are read; templates and code never land) and package export round trip |

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
  limited to one application instance and controlled public presentation, not distributed/multi-instance
  deployment. Use registered independent DNS domains/subdomains (IPv4/localhost for local use,
  no IPv6 literal registration); DNS/TLS/reverse proxy configuration remains an operations task.
- All uploaded images are public, including those referenced only by drafts. Site directory
  isolation is not private-attachment authorization.
- Non-image uploads accept whitelisted formats only (PDF, audio, video, XLSX, CSV). CSV must be
  plain UTF-8 text (BOM allowed); other encodings such as Big5/GBK are rejected. XLSX is validated
  by OOXML container signature only and stored verbatim; the server never parses cell contents.
- The Data Protection key ring is encrypted with Windows DPAPI, so it can only be decrypted on the
  same machine by the same user. Recovery on another machine requires re-provisioning. Keys remain
  global and are not moved by this batch.
- A data export (JSON or ZIP) contains the password hashes of protected content, which can be attacked offline.
  Treat the file as a secret and keep it out of version control and shared drives. An import
  replaces every piece of text data on the current site in one transaction. A JSON import leaves image
  and non-image files untouched; a ZIP import restores images and variants under `media/` and non-image
  files under `files/`, then trims media files outside the manifest (replace semantics), with a 256 MB
  media cap. "Full backup" here means that site's content-plus-media archive only: no accounts,
  memberships, PATs or global keys. It cannot replace full PostgreSQL,
  media and key backups.
- ZIP restore serializes concurrent media mutations with a PostgreSQL advisory lock and restores original
  files after ordinary I/O failures. The filesystem and database transaction are not process-crash atomic:
  a power loss or process termination during media replacement requires restoration from a full backup.
  Site deletion uses a database transaction; if physical directory removal fails, the orphan directory is
  retained for operational retry rather than reporting a failed database deletion.
- WXR export is a migration path for articles, pages, categories, tags and approved comments; it does not promise
  plugin-data compatibility. WordPress WXR import is additive, preserves recognizable
  published/pending/draft/private/trash states and skips duplicate slugs. Attachment download is off by
  default; when enabled it requires an allowed-host list and follows HTTPS/SSRF/size limits.
  Markdown/Medium portable import is additive, imports as drafts by default and never downloads external
  images. The input file and the ZIP's expanded total are each capped at 32 MiB, with at most 200 Markdown/HTML
  source files per run. WebDAV only stores the same site ZIP off-site; it still excludes the database, accounts and keys,
  accepts HTTPS only and never follows redirects.
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
- No arbitrary executable plugins, free-form theme templates, SSO or private attachments. No PHP
  runtime or WordPress table/REST API/executable-plugin compatibility layer. Comments, the menu
  editor, controlled themes and author review workflows are implemented, but are not a WordPress
  compatibility layer.
- Custom CSS is sanitized, not a free-form template: @import, @font-face, expression, javascript:
  and backslash escapes are rejected, and overriding built-in theme variables requires `!important`;
  `url()` may reference on-site or external image resources at the same trust level as inline images.
  Widgets are limited to the fifteen built-in declarative types — no arbitrary code or custom block types.
- The controlled package market ships declarative extension packs only (theme packs and widget packs),
  sourced from the built-in catalog and ZIP import: font stacks accept the allow-listed sans / serif /
  mono keys mapped to built-in constants, and a ZIP may contain only a root-level manifest.json (smuggled
  entries and path traversal are rejected). There is no online catalog fetch and no component, script or
  external font is ever loaded. Disabling or uninstalling a theme pack falls the layout back to the
  built-in Reading preset; uninstalling a widget pack also deletes the widgets it created.
- Compatibility with other CMS markets is limited to the declarative layer: a third-party (WordPress) theme archive converts into a controlled theme pack, but only the style.css header metadata plus a derivable accent color and font family are read; templates and code are never kept or executed, and third-party stylesheets are not carried over. Executable plugins from other CMSs are not supported at all — compatibility is never bought by loading arbitrary code. There is no remote catalog integration either (no connection to wp.org or similar online markets); compatibility covers locally uploaded archives only.
- `AddSiteIsolationAndApiCredentials` has not been applied to production. Once sites/accounts/
  memberships/credentials are used, `Down` refuses a downgrade; plan full backups and recovery
  separately before deployment or rollback.

## License

MIT — see [LICENSE](LICENSE).
