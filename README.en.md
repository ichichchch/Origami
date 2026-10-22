# WordPress.NET

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
| Administrator | One securely provisioned administrator, cookie sign-in, sign-out, server-side password recovery |
| Articles | List, create, edit, draft, publish, withdraw, confirmed permanent delete |
| Pages | Standalone pages with their own URLs, never mixed into the article list |
| Categories & tags | Flat structure, many-to-many assignment, deletion never removes articles |
| Media | Image upload, media library, insert as Markdown, public image URLs |
| Public site | Article list with pagination, article and page detail, basic navigation, mobile-friendly |
| Site settings | Site name and description, applied immediately to the public site |

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
export ConnectionStrings__Cms="Host=127.0.0.1;Port=5432;Database=wordpressnet;Username=wordpressnet;Password=<your password>"
export Storage__RootPath="D:/wpnet-storage"   # optional; defaults to a folder under the user profile
```

`Storage__RootPath` must be an absolute path outside the published application directory. It holds
uploaded images and the Data Protection key ring.

### 2. Apply the schema

```bash
dotnet run --project src/WordPressNet.Web -- migrate
```

The schema is managed by EF Core migrations. Upgrades never drop and recreate the database.

### 3. Create the administrator

```bash
dotnet run --project src/WordPressNet.Web -- admin-init
```

The password is read from an interactive console without echo. Credentials are never accepted as
command arguments or through redirected input, and there is no public registration or
initialization endpoint. `admin-reset` recovers access on the server and immediately revokes
existing sessions.

### 4. Run

```bash
dotnet run --project src/WordPressNet.Web
```

The public site is at `/`, the admin area at `/admin/posts`.

## Project layout

```text
src/WordPressNet.Web/
  Components/
    Pages/          # Public site (Static SSR): home, article and page detail, 404, error page
    Admin/          # Admin area (Interactive Server): content, taxonomies, media, settings
    Account/        # Sign-in, sign-out, identity revalidation
    Layout/         # Public and admin layouts, reconnection notice
  Data/             # DbContext, Identity registration, migrations
  Models/           # Content, Category, Tag, MediaAsset, SiteSettings, AdminUser
  Services/         # Content, taxonomies, media, Markdown, settings, authorization, admin commands
  wwwroot/          # Bundled CSS; persistent uploads never live here
tests/WordPressNet.Tests/
```

## Data model

| Model | Responsibility | Database constraints |
| --- | --- | --- |
| `AdminUser` (Identity) | Account, password hash, security stamp | `SingletonKey` unique and checked to be `1`, so exactly one administrator can exist |
| `Content` | Articles and pages (`Kind`), title, slug, Markdown body, status, timestamps, version | Slug unique site-wide; check constraints on status, title, slug and publish time; `Version` is the concurrency token |
| `Category` / `Tag` | Article categories and tags | Name and slug unique within their kind |
| `ContentCategories` / `ContentTags` | Many-to-many links | Removed together with the content |
| `MediaAsset` | File identity, media type, size, dimensions, upload time | File name unique; image bytes live on disk |
| `SiteSettings` | Single site name and description | Only `Id = 1` is allowed |

Status is either `Draft` or `Published`; withdrawing returns content to `Draft`. The first publish
time is written once and preserved across later edits, withdrawals and re-publishes.

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
- Sign-in and sign-out are ordinary HTTP POSTs with antiforgery validation.

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
dotnet test tests/WordPressNet.Tests/WordPressNet.Tests.csproj -c Release
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

The browser tests drive real Edge or Chrome through Playwright; the persistence test performs an
actual `pg_dump` / `pg_restore` cycle.

## Known limitations

- Single site, single administrator, single application instance. No roles, multi-user or
  multi-site support.
- All uploaded images are public, including those referenced only by drafts.
- The Data Protection key ring is encrypted with Windows DPAPI, so it can only be decrypted on the
  same machine by the same user. Recovery on another machine requires re-provisioning.
- No comments, plugins, scheduled publishing, autosave, revision history, recycle bin, full-text
  search, media deletion or menu editor.

## License

MIT — see [LICENSE](LICENSE).
