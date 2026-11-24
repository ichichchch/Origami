namespace Origami.Core.Services;

/// <summary>新增式匯入 Markdown 與 Medium HTML／ZIP；不取代現有內容，也不下載匯入檔中的圖片。</summary>
public sealed partial class PortableImportService(
    IDbContextFactory<CmsDbContext> factory, AdminAccess admin, ContentService content,
    TaxonomyService taxonomy, MarkdownService markdown, TimeProvider clock)
{
    public sealed record PortableItem(string Source, string Title, string Slug, ContentKind Kind,
        ContentStatus Status, IReadOnlyList<string> Categories, IReadOnlyList<string> Tags);
    public sealed record PortablePreview(int Posts, int Pages, int Categories, int Tags,
        IReadOnlyList<PortableItem> Items, IReadOnlyList<string> Warnings);
    public sealed record PortableResult(int Imported, int Skipped, IReadOnlyList<string> Warnings);

    public async Task<PortablePreview> AnalyzeAsync(Stream input, string fileName, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var batch = await ParseAsync(input, fileName, ct);
        return await DescribeAsync(batch, ct);
    }

    public async Task<PortableResult> ApplyAsync(Stream input, string fileName, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var batch = await ParseAsync(input, fileName, ct);
        var existing = await ExistingSlugsAsync(ct);
        var warnings = batch.Warnings.ToList();
        var imported = 0;
        var skipped = 0;
        var now = clock.GetUtcNow();
        foreach (var item in batch.Items)
        {
            if (existing.Contains(item.Slug))
            {
                warnings.Add($"略過 {item.Source}：網址代稱 {item.Slug} 已存在。");
                skipped++;
                continue;
            }
            try
            {
                var categoryIds = new List<Guid>();
                var tagIds = new List<Guid>();
                if (item.Kind == ContentKind.Post)
                {
                    foreach (var name in item.Categories)
                        categoryIds.Add(await EnsureTermAsync(TaxonomyKind.Category, name, ct));
                    foreach (var name in item.Tags)
                        tagIds.Add(await EnsureTermAsync(TaxonomyKind.Tag, name, ct));
                }
                else if (item.Categories.Count > 0 || item.Tags.Count > 0)
                {
                    warnings.Add($"{item.Source} 是頁面，分類與標籤未套用。");
                }

                var importedStatus = item.Status;
                if (item.Date is { } importedDate && importedDate > now && importedStatus == ContentStatus.Published)
                {
                    importedStatus = ContentStatus.Draft;
                    warnings.Add($"{item.Source} 的發布日期在未來，已匯入為草稿；請在編輯器確認排程。");
                }
                var created = await content.CreateAsync(item.Kind, new ContentEdit
                {
                    Title = item.Title,
                    Slug = item.Slug,
                    Markdown = item.Markdown,
                    Excerpt = string.IsNullOrWhiteSpace(item.Excerpt) ? markdown.Excerpt(item.Markdown) : item.Excerpt,
                    Status = importedStatus,
                    CategoryIds = categoryIds,
                    TagIds = tagIds,
                    Language = item.Language,
                    MetaDescription = item.MetaDescription,
                    NoIndex = item.NoIndex,
                    IsSticky = item.IsSticky
                }, ct);
                await ApplyDatesAsync(created.Id!.Value, item, importedStatus, ct);
                existing.Add(item.Slug);
                imported++;
            }
            catch (CmsException error)
            {
                warnings.Add($"略過 {item.Source}：{error.Message}");
                skipped++;
            }
        }
        return new(imported, skipped, warnings);
    }

    private async Task<PortablePreview> DescribeAsync(PortableBatch batch, CancellationToken ct)
    {
        var existing = await ExistingSlugsAsync(ct);
        var warnings = batch.Warnings.ToList();
        var accepted = new List<PortableItem>();
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in batch.Items)
        {
            if (existing.Contains(item.Slug) || !slugs.Add(item.Slug))
            {
                warnings.Add($"略過 {item.Source}：網址代稱 {item.Slug} 重複。");
                continue;
            }
            accepted.Add(new(item.Source, item.Title, item.Slug, item.Kind, item.Status, item.Categories, item.Tags));
        }
        var posts = accepted.Where(x => x.Kind == ContentKind.Post).ToList();
        return new(posts.Count, accepted.Count(x => x.Kind == ContentKind.Page),
            posts.SelectMany(x => x.Categories).Distinct(StringComparer.Ordinal).Count(),
            posts.SelectMany(x => x.Tags).Distinct(StringComparer.Ordinal).Count(), accepted, warnings);
    }

    private async Task<HashSet<string>> ExistingSlugsAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return [.. await db.Contents.AsNoTracking().Select(x => x.Slug).ToListAsync(ct)];
    }

    private async Task<Guid> EnsureTermAsync(TaxonomyKind kind, string name, CancellationToken ct)
    {
        var slug = ContentRules.NormalizeSlug(name);
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = kind == TaxonomyKind.Category
            ? await db.Categories.AsNoTracking().Where(x => x.Slug == slug).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            : await db.Tags.AsNoTracking().Where(x => x.Slug == slug).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (existing is { } id) return id;
        var saved = await taxonomy.SaveAsync(kind, new TaxonomyEdit { Name = name, Slug = slug }, ct);
        return saved.Id!.Value;
    }

    private async Task ApplyDatesAsync(Guid id, ParsedPortableItem item, ContentStatus status, CancellationToken ct)
    {
        if (item.Date is null) return;
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Contents.Where(x => x.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.CreatedAt, item.Date.Value)
            .SetProperty(x => x.UpdatedAt, item.Date.Value)
            .SetProperty(x => x.PublishedAt, status == ContentStatus.Published ? item.Date.Value : null), ct);
    }

    private sealed class ParsedPortableItem
    {
        public required string Source { get; init; }
        public required string Title { get; init; }
        public required string Slug { get; init; }
        public required string Markdown { get; init; }
        public required ContentKind Kind { get; init; }
        public required ContentStatus Status { get; init; }
        public string Excerpt { get; init; } = "";
        public string Language { get; init; } = "";
        public string MetaDescription { get; init; } = "";
        public bool NoIndex { get; init; }
        public bool IsSticky { get; init; }
        public DateTimeOffset? Date { get; init; }
        public IReadOnlyList<string> Categories { get; init; } = [];
        public IReadOnlyList<string> Tags { get; init; } = [];
    }

    private sealed record PortableBatch(IReadOnlyList<ParsedPortableItem> Items, IReadOnlyList<string> Warnings);

    private async Task<PortableBatch> ParseAsync(Stream input, string fileName, CancellationToken ct)
    {
        using var payload = await ArchiveGuard.BufferAsync(input, Limits.PortableImportMaxBytes,
            $"匯入檔大小須為 1 位元組至 {Limits.PortableImportMaxBytes} 位元組。", ct);
        var bytes = payload.GetBuffer();
        var isZip = payload.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        var sources = new List<(string Name, byte[] Data)>();
        var warnings = new List<string>();
        if (isZip)
        {
            payload.Position = 0;
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > Limits.PortableImportMaxFiles * 4)
                throw new CmsException($"ZIP 條目過多，最多允許 {Limits.PortableImportMaxFiles * 4} 個。");
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || !IsSupportedEntry(entry.FullName)) continue;
                if (HasUnsafeArchivePath(entry.FullName)) throw new CmsException("ZIP 含有不安全的檔案路徑。");
                using var expanded = new MemoryStream();
                total = await ArchiveGuard.CopyBoundedAsync(entry.Open(), expanded, total, Limits.PortableImportMaxBytes,
                    $"ZIP 解壓後總量超過 {Limits.PortableImportMaxBytes / 1024 / 1024} MB 上限。", ct);
                sources.Add((entry.FullName, expanded.ToArray()));
                if (sources.Count > Limits.PortableImportMaxFiles)
                    throw new CmsException($"單次最多匯入 {Limits.PortableImportMaxFiles} 個 Markdown 或 HTML 檔案。");
            }
        }
        else
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var head = System.Text.Encoding.UTF8.GetString(bytes, 0, (int)Math.Min(payload.Length, 256)).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            var isHtml = extension is ".html" or ".htm" || head.StartsWith('<');
            if (extension.Length > 0 && extension is not (".md" or ".markdown" or ".html" or ".htm"))
                throw new CmsException("請選擇 .md、.markdown、.html 或包含這些檔案的 ZIP。");
            sources.Add((string.IsNullOrWhiteSpace(fileName) ? isHtml ? "medium.html" : "import.md" : fileName, payload.ToArray()));
        }
        if (sources.Count == 0) throw new CmsException("ZIP 內沒有可匯入的 Markdown 或 HTML 檔案。");

        var items = new List<ParsedPortableItem>();
        foreach (var source in sources)
        {
            try
            {
                var text = System.Text.Encoding.UTF8.GetString(source.Data).TrimStart('\uFEFF');
                var isHtml = Path.GetExtension(source.Name) is ".html" or ".htm" || text.TrimStart().StartsWith('<');
                items.Add(isHtml ? ParseMediumHtml(text, source.Name) : ParseMarkdown(text, source.Name));
            }
            catch (CmsException error)
            {
                warnings.Add($"略過 {source.Name}：{error.Message}");
            }
        }
        return new(items, warnings);
    }

    private static bool IsSupportedEntry(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".md" or ".markdown" or ".html" or ".htm";

    private static bool HasUnsafeArchivePath(string name)
    {
        if (name.StartsWith('/') || name.StartsWith('\\') || name.Contains(':')) return true;
        return name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Any(x => x is "." or "..");
    }

    private ParsedPortableItem ParseMarkdown(string text, string source)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end >= 0)
            {
                foreach (var line in text[4..end].Split('\n'))
                {
                    var colon = line.IndexOf(':', StringComparison.Ordinal);
                    if (colon <= 0) continue;
                    metadata[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }
                text = text[(end + 4)..].TrimStart('\n');
            }
        }
        var title = Value(metadata, "title") ?? "";
        var heading = MarkdownHeading().Match(text);
        if (title.Length == 0 && heading.Success)
        {
            title = heading.Groups[1].Value.Trim();
            text = text.Remove(heading.Index, heading.Length).TrimStart();
        }
        if (title.Length == 0) title = Path.GetFileNameWithoutExtension(source).Replace('-', ' ').Replace('_', ' ').Trim();
        return BuildItem(source, title, Value(metadata, "slug"), text, metadata, null);
    }

    private ParsedPortableItem ParseMediumHtml(string html, string source)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        var title = document.QuerySelector("h1.title")?.TextContent.Trim()
            ?? document.QuerySelector("h1")?.TextContent.Trim()
            ?? document.QuerySelector("title")?.TextContent.Trim()
            ?? "";
        var contentNode = document.QuerySelector(".postArticle-content, .section-content, article, main") ?? document.Body
            ?? throw new CmsException("HTML 沒有可讀取的內容。");
        var excerpt = document.QuerySelector(".subtitle")?.TextContent.Trim() ?? "";
        var markdown = ConvertHtml(contentNode);
        if (title.Length == 0) title = Path.GetFileNameWithoutExtension(source).Replace('-', ' ').Trim();
        return BuildItem(source, title, null, markdown, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), excerpt);
    }

    private ParsedPortableItem BuildItem(string source, string title, string? slug, string body,
        IReadOnlyDictionary<string, string> metadata, string? htmlExcerpt)
    {
        title = title.Trim();
        if (title.Length == 0 || title.Length > Limits.Title) throw new CmsException($"標題須為 1–{Limits.Title} 字。");
        if (body.Length > Limits.Body) throw new CmsException($"本文超過 {Limits.Body} 字上限。");
        slug = string.IsNullOrWhiteSpace(slug) ? title : slug;
        var normalizedSlug = ContentRules.NormalizeSlug(slug);
        var rawKind = Value(metadata, "type") ?? Value(metadata, "kind") ?? "post";
        var kind = rawKind.ToLowerInvariant() is "page" or "頁面" ? ContentKind.Page : ContentKind.Post;
        var rawStatus = Value(metadata, "status") ?? "draft";
        var status = rawStatus.ToLowerInvariant() switch
        {
            "publish" or "published" or "發佈" or "发布" => ContentStatus.Published,
            "pending" or "待審" or "待审" => ContentStatus.Pending,
            _ => ContentStatus.Draft
        };
        var dateText = Value(metadata, "date") ?? Value(metadata, "publishedAt") ?? Value(metadata, "published_at");
        DateTimeOffset? date = null;
        if (!string.IsNullOrWhiteSpace(dateText))
        {
            if (!DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                throw new CmsException("日期格式無效，請使用 ISO 8601。");
            date = parsed;
        }
        var language = Value(metadata, "lang") ?? Value(metadata, "language") ?? "";
        if (language.Length > 0)
        {
            try { language = CultureInfo.GetCultureInfo(language).Name; }
            catch (CultureNotFoundException) { throw new CmsException("內容語言格式無效。"); }
        }
        var excerpt = Value(metadata, "excerpt") ?? htmlExcerpt ?? "";
        if (excerpt.Length > Limits.Excerpt) excerpt = excerpt[..Limits.Excerpt];
        var meta = Value(metadata, "description") ?? Value(metadata, "metaDescription") ?? "";
        if (meta.Length > Limits.MetaDescription) meta = meta[..Limits.MetaDescription];
        return new ParsedPortableItem
        {
            Source = source, Title = title, Slug = normalizedSlug, Markdown = body.Trim(), Kind = kind, Status = status,
            Excerpt = excerpt, Language = language, MetaDescription = meta,
            NoIndex = Value(metadata, "noindex")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IsSticky = Value(metadata, "sticky")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            Date = date, Categories = ListValue(metadata, "categories", "category"), Tags = ListValue(metadata, "tags", "tag")
        };
    }

    private static string? Value(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && value.Length > 0 ? Unquote(value) : null;

    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1] : value;
    }

    private static IReadOnlyList<string> ListValue(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            raw = raw.Trim();
            if (raw.StartsWith('[') && raw.EndsWith(']'))
            {
                try
                {
                    var array = JsonSerializer.Deserialize<string[]>(raw) ?? [];
                    return [.. array.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).Take(50)];
                }
                catch (JsonException) { /* 退回逗號分隔格式。 */ }
            }
            return [.. raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unquote).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Take(50)];
        }
        return [];
    }

    private static string ConvertHtml(IElement root)
    {
        var blocks = new List<string>();
        foreach (var node in root.ChildNodes)
        {
            if (node is IText text)
            {
                var value = Escape(text.TextContent);
                if (value.Trim().Length > 0) blocks.Add(value.Trim());
            }
            else if (node is IElement element) blocks.Add(ConvertElement(element));
        }
        return string.Join("\n\n", blocks.Where(x => x.Trim().Length > 0)).Trim();
    }

    private static string ConvertElement(IElement element)
    {
        var tag = element.LocalName.ToLowerInvariant();
        if (tag is "script" or "style" or "noscript" or "svg" or "form" or "button" or "iframe") return "";
        if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            return new string('#', tag[1] - '0') + " " + Inline(element).Trim();
        if (tag is "p" or "figcaption")
            return Inline(element).Trim();
        if (tag is "div" or "section" or "header" or "footer" or "article")
            return element.QuerySelectorAll("p, div, section, h1, h2, h3, h4, h5, h6, ul, ol, pre, blockquote, figure, img").Count > 0
                ? ConvertHtml(element) : Inline(element).Trim();
        if (tag == "br") return "";
        if (tag == "hr") return "---";
        if (tag == "blockquote")
            return string.Join("\n", Inline(element).Trim().Split('\n').Select(x => "> " + x));
        if (tag is "ul" or "ol")
        {
            var index = 1;
            var lines = new List<string>();
            foreach (var child in element.Children.Where(x => x.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
            {
                var prefix = tag == "ol" ? index++ + ". " : "- ";
                lines.Add(prefix + Inline(child).Trim().Replace("\n", "\n  "));
            }
            return string.Join("\n", lines);
        }
        if (tag == "pre")
        {
            var code = element.QuerySelector("code");
            var language = code?.ClassList.FirstOrDefault(x => x.StartsWith("language-", StringComparison.Ordinal))?["language-".Length..] ?? "";
            var text = (code ?? element).TextContent.Trim('\r', '\n');
            var fence = text.Contains("```", StringComparison.Ordinal) ? "````" : "```";
            return fence + language + "\n" + text + "\n" + fence;
        }
        if (tag == "img")
        {
            var src = element.GetAttribute("src") ?? "";
            if (!Uri.TryCreate(src, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "";
            var alt = Escape(element.GetAttribute("alt") ?? "");
            return "![" + alt + "](" + uri + ")";
        }
        if (tag == "figure") return ConvertHtml(element);
        if (tag == "a") return Inline(element);
        return Inline(element).Trim();
    }

    private static string Inline(IElement element)
    {
        var result = new StringBuilder();
        foreach (var node in element.ChildNodes)
        {
            if (node is IText text) result.Append(Escape(text.TextContent));
            else if (node is IElement child)
            {
                var tag = child.LocalName.ToLowerInvariant();
                if (tag is "script" or "style" or "noscript" or "svg" or "form" or "button" or "iframe") continue;
                if (tag == "br") { result.Append("  \n"); continue; }
                if (tag == "img")
                {
                    var image = ConvertElement(child);
                    if (image.Length > 0) result.Append(image);
                    continue;
                }
                var inner = Inline(child);
                if (inner.Length == 0) continue;
                if (tag == "a")
                {
                    var href = child.GetAttribute("url") ?? child.GetAttribute("href");
                    result.Append(IsSafeLink(href) ? "[" + inner.Trim() + "](" + href + ")" : inner);
                    continue;
                }
                result.Append(tag switch
                {
                    "strong" or "b" => "**" + inner.Trim() + "**",
                    "em" or "i" => "*" + inner.Trim() + "*",
                    "del" or "s" => "~~" + inner.Trim() + "~~",
                    "code" => "`" + inner.Trim().Replace('`', '\'') + "`",
                    _ => inner
                });
            }
        }
        return result.ToString();
    }

    private static bool IsSafeLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string Escape(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("*", "\\*", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal);

    [GeneratedRegex(@"(?m)^#\s+(.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownHeading();
}
