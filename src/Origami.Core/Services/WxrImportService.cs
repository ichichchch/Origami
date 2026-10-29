namespace Origami.Core.Services;

/// <summary>
/// WordPress WXR（1.0／1.2／2.0）匯入與匯出：解析或輸出文章、頁面、分類與標籤。
/// 匯入是「新增」而非「取代」，逐項處理：單筆失敗只記錄警告，不影響其他項目。
/// 支援選用的附件下載遷移（含 HTTPS/SSRF 檢查、白名單網域、單檔大小與總筆數上限）。
/// </summary>
public sealed class WxrImportService(
    IDbContextFactory<CmsDbContext> factory,
    AdminAccess admin,
    TimeProvider clock,
    MediaService? mediaService = null,
    MarkdownService? markdownService = null,
    IHttpClientFactory? httpClients = null)
{
    /// <summary>WXR 檔案大小上限；避免匯入佔滿記憶體。</summary>
    public const int MaxBytes = 64 * 1024 * 1024;

    public sealed record WxrPreview(int Posts, int Pages, int Categories, int Tags, int Attachments,
        IReadOnlyList<string> Warnings);

    public sealed record WxrResult(int Imported, int Skipped, IReadOnlyList<string> Warnings, int AttachmentsImported = 0);

    public sealed record WxrAttachmentOptions(bool DownloadAttachments = false, string? AllowedHosts = null);

    public async Task<WxrPreview> AnalyzeAsync(Stream input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var document = await LoadAsync(input, ct);
        var items = ItemsOf(document);
        int posts = 0, pages = 0, attachments = 0, categories = 0, tags = 0;
        foreach (var item in items)
        {
            var type = Text(item, "post_type");
            switch (type)
            {
                case "post": posts++; break;
                case "page": pages++; break;
                case "attachment": attachments++; break;
            }
        }
        foreach (var term in DeclaredTerms(document))
        {
            if (term.IsTag) tags++;
            else categories++;
        }
        var warnings = new List<string>();
        if (items.Count == 0) warnings.Add("檔案內沒有可匯入的項目。");
        if (attachments > 0) warnings.Add($"附件（{attachments} 筆）不含二進位內容，未建立媒體資產；請另外上傳媒體檔。");
        return new(posts, pages, categories, tags, attachments, warnings);
    }

    /// <summary>把 WXR 內容寫入目前站點；回傳成功筆數、略過筆數與警告清單。可選開啟附件下載遷移。</summary>
    public async Task<WxrResult> ImportAsync(Stream input, CancellationToken ct = default, WxrAttachmentOptions? attachmentOptions = null)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        var document = await LoadAsync(input, ct);
        var warnings = new List<string>();
        var imported = 0;
        var skipped = 0;
        var attachmentsImported = 0;
        var urlMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 若啟用附件下載遷移，先處理 attachment 項目並建立舊 URL -> /media/{fileName} 對照表。
        if (attachmentOptions?.DownloadAttachments == true && mediaService is not null)
        {
            var allowedHosts = ParseAllowedHosts(attachmentOptions.AllowedHosts);
            var attachmentItems = ItemsOf(document)
                .Where(x => Text(x, "post_type") == "attachment")
                .Take(Limits.WxrAttachmentMaxCount)
                .ToList();

            foreach (var att in attachmentItems)
            {
                ct.ThrowIfCancellationRequested();
                var rawUrl = TextAny(att, "attachment_url", "guid");
                if (rawUrl.Length == 0) continue;
                if (!IsSafeAttachmentUrl(rawUrl, allowedHosts, out var safeUri, out var reason))
                {
                    warnings.Add($"附件「{rawUrl}」已略過：{reason}");
                    continue;
                }
                try
                {
                    using var client = httpClients is not null
                        ? httpClients.CreateClient("OrigamiWxrAttachment")
                        : new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                    client.Timeout = TimeSpan.FromSeconds(15);
                    using var response = await client.GetAsync(safeUri, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        warnings.Add($"附件「{rawUrl}」下載失敗（HTTP {(int)response.StatusCode}）。");
                        continue;
                    }
                    await using var respStream = await response.Content.ReadAsStreamAsync(ct);
                    using var ms = new MemoryStream();
                    var buf = new byte[81920];
                    int r;
                    var overLimit = false;
                    while ((r = await respStream.ReadAsync(buf, ct)) > 0)
                    {
                        if (ms.Length + r > Limits.FileUploadBytes)
                        {
                            overLimit = true;
                            break;
                        }
                        await ms.WriteAsync(buf.AsMemory(0, r), ct);
                    }
                    if (overLimit || ms.Length == 0)
                    {
                        warnings.Add($"附件「{rawUrl}」為空或超過單檔上限，已略過。");
                        continue;
                    }
                    ms.Position = 0;
                    var fileName = Path.GetFileName(safeUri!.LocalPath);
                    if (string.IsNullOrWhiteSpace(fileName)) fileName = "attachment.png";
                    var ext = Path.GetExtension(fileName).ToLowerInvariant();
                    MediaItem uploaded = ext is ".png" or ".jpg" or ".jpeg" or ".webp"
                        ? await mediaService.UploadAsync(ms, fileName, ct)
                        : await mediaService.UploadFileAsync(ms, fileName, ct);
                    urlMap[rawUrl] = uploaded.Url;
                    attachmentsImported++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add($"附件「{rawUrl}」匯入失敗：{ex.Message}");
                }
            }
        }

        // 分類與標籤：WXR 以 wp:category（category_nicename／cat_name）與 wp:tag
        // （tag_slug／tag_name）宣告，依代稱對應既有項目，沒有則建立。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            foreach (var term in DeclaredTerms(document))
            {
                if (!seen.Add((term.IsTag ? "tag:" : "category:") + term.Slug)) continue;
                var slug = NormalizeSlug(term.Slug, term.Name);
                if (slug is null)
                {
                    warnings.Add($"分類法「{term.Name}」的代稱無法正規化，已略過。");
                    continue;
                }
                if (term.IsTag)
                {
                    if (!await db.Tags.AsNoTracking().AnyAsync(x => x.Slug == slug, ct))
                        db.Tags.Add(new Tag { SiteId = db.CurrentSiteId, Name = Trim(term.Name, Limits.TaxonomyName), Slug = slug, Description = "" });
                }
                else if (!await db.Categories.AsNoTracking().AnyAsync(x => x.Slug == slug, ct))
                {
                    db.Categories.Add(new Category { SiteId = db.CurrentSiteId, Name = Trim(term.Name, Limits.TaxonomyName), Slug = slug, Description = "" });
                }
            }
            await ContentRules.SaveAsync(db, ct);
        }

        foreach (var item in ItemsOf(document))
        {
            ct.ThrowIfCancellationRequested();
            var type = Text(item, "post_type");
            if (type is not ("post" or "page")) continue;
            var title = Trim(Text(item, "title"), Limits.Title);
            if (title.Length == 0) { skipped++; warnings.Add("有一筆項目缺少標題，已略過。"); continue; }
            var slug = Text(item, "post_name");
            string normalizedSlug;
            try
            {
                normalizedSlug = slug.Length > 0 ? ContentRules.NormalizeSlug(slug) : ContentRules.NormalizeSlug(title);
            }
            catch (CmsException)
            {
                skipped++;
                warnings.Add($"「{title}」的代稱無法正規化，已略過。");
                continue;
            }

            await using var db = await factory.CreateDbContextAsync(ct);
            if (await db.Contents.AsNoTracking().AnyAsync(x => x.Slug == normalizedSlug, ct))
            {
                skipped++;
                warnings.Add($"代稱「{normalizedSlug}」已存在，已略過。");
                continue;
            }

            var status = TextAny(item, "status", "post_status") switch
            {
                "publish" => ContentStatus.Published,
                "pending" => ContentStatus.Pending,
                "trash" => ContentStatus.Trashed,
                _ => ContentStatus.Draft
            };
            var isPrivate = TextAny(item, "status", "post_status") == "private";
            var publishedAt = ParseDate(Text(item, "post_date"));
            var markdown = HtmlToMarkdown.Convert(Text(item, "encoded"));
            foreach (var (oldUrl, newLocalUrl) in urlMap)
                markdown = markdown.Replace(oldUrl, newLocalUrl, StringComparison.OrdinalIgnoreCase);
            var content = new Content
            {
                SiteId = db.CurrentSiteId,
                Kind = type == "page" ? ContentKind.Page : ContentKind.Post,
                Title = title,
                Slug = normalizedSlug,
                Markdown = Trim(markdown, Limits.Body),
                Excerpt = "",
                Status = status,
                IsPrivate = isPrivate,
                AuthorName = Trim(Text(item, "creator"), Limits.AuthorName),
                CreatedAt = publishedAt ?? clock.GetUtcNow(),
                UpdatedAt = publishedAt ?? clock.GetUtcNow(),
                PublishedAt = status == ContentStatus.Published ? publishedAt ?? clock.GetUtcNow() : null
            };
            // 內容識別由伺服器產生；作者帳號不存在，只保留署名快照。
            if (content.AuthorName.Length > 0)
            {
                try { content.AuthorSlug = ContentRules.NormalizeSlug(content.AuthorName); }
                catch (CmsException) { content.AuthorSlug = ""; }
            }

            foreach (var category in item.Elements().Where(x => x.Name.LocalName == "category"))
            {
                var domain = category.Attribute("domain")?.Value ?? "category";
                var nicename = category.Attribute("nicename")?.Value ?? "";
                if (nicename.Length == 0) continue;
                var termSlug = NormalizeSlug(nicename, category.Value.Trim());
                if (termSlug is null) { warnings.Add($"「{title}」的一個分類法代稱無法正規化，已略過該項關聯。"); continue; }
                // 項目引用了未宣告的分類或標籤時以項目提供的名稱建立，行為與 WordPress 匯入一致。
                var name = Trim(category.Value.Trim().Length > 0 ? category.Value.Trim() : termSlug, Limits.TaxonomyName);
                if (domain == "post_tag")
                {
                    var tag = await db.Tags.FirstOrDefaultAsync(x => x.Slug == termSlug, ct);
                    if (tag is null)
                    {
                        tag = new Tag { SiteId = db.CurrentSiteId, Name = name, Slug = termSlug, Description = "" };
                        db.Tags.Add(tag);
                    }
                    content.Tags.Add(tag);
                }
                else
                {
                    var categoryEntity = await db.Categories.FirstOrDefaultAsync(x => x.Slug == termSlug, ct);
                    if (categoryEntity is null)
                    {
                        categoryEntity = new Category { SiteId = db.CurrentSiteId, Name = name, Slug = termSlug, Description = "" };
                        db.Categories.Add(categoryEntity);
                    }
                    content.Categories.Add(categoryEntity);
                }
            }

            db.Contents.Add(content);
            try
            {
                await ContentRules.SaveAsync(db, ct);
                imported++;
                // 已發布內容與一般發布一樣重新進入嵌入管道。
                if (content.Status == ContentStatus.Published)
                {
                    db.EmbeddingJobs.Add(new EmbeddingJob
                    {
                        SiteId = db.CurrentSiteId, ContentId = content.Id,
                        EnqueuedAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow()
                    });
                    await ContentRules.SaveAsync(db, ct);
                }
            }
            catch (CmsException error)
            {
                skipped++;
                warnings.Add($"「{title}」匯入失敗：{error.Message}");
            }
        }
        return new(imported, skipped, warnings, attachmentsImported);
    }

    /// <summary>將目前站點的分類、標籤、文章、獨立頁面與已核可評論匯出為標準 WordPress WXR 1.2 XML。</summary>
    public async Task ExportAsync(Stream output, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(x => x.Slug).ToListAsync(ct);
        var tags = await db.Tags.AsNoTracking().OrderBy(x => x.Slug).ToListAsync(ct);
        var contents = await db.Contents.AsNoTracking().AsSplitQuery()
            .Include(x => x.Categories)
            .Include(x => x.Tags)
            .Where(x => x.Status != ContentStatus.Trashed && x.ContentTypeId == null)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync(ct);
        var commentsByContent = (await db.Comments.AsNoTracking()
            .Where(x => x.Status == CommentStatus.Approved)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync(ct))
            .ToLookup(x => x.ContentId);

        XNamespace contentNs = "http://purl.org/rss/1.0/modules/content/";
        XNamespace excerptNs = "http://wordpress.org/export/1.2/excerpt/";
        XNamespace dcNs = "http://purl.org/dc/elements/1.1/";
        XNamespace wpNs = "http://wordpress.org/export/1.2/";
        var md = markdownService ?? new MarkdownService();
        var catSlugById = categories.ToDictionary(x => x.Id, x => x.Slug);

        var channel = new XElement("channel",
            new XElement("title", settings?.Name ?? "Origami"),
            new XElement("link", "https://localhost/"),
            new XElement("description", settings?.Description ?? ""),
            new XElement("pubDate", clock.GetUtcNow().UtcDateTime.ToString("R")),
            new XElement("language", "zh-TW"),
            new XElement(wpNs + "wxr_version", "1.2"));

        var termIndex = 1;
        foreach (var cat in categories)
        {
            channel.Add(new XElement(wpNs + "category",
                new XElement(wpNs + "term_id", termIndex++),
                new XElement(wpNs + "category_nicename", new XCData(cat.Slug)),
                new XElement(wpNs + "category_parent", cat.ParentId is { } pid && catSlugById.TryGetValue(pid, out var pSlug) ? pSlug : ""),
                new XElement(wpNs + "cat_name", new XCData(cat.Name)),
                !string.IsNullOrEmpty(cat.Description) ? new XElement(wpNs + "category_description", new XCData(cat.Description)) : null));
        }

        foreach (var tag in tags)
        {
            channel.Add(new XElement(wpNs + "tag",
                new XElement(wpNs + "term_id", termIndex++),
                new XElement(wpNs + "tag_slug", new XCData(tag.Slug)),
                new XElement(wpNs + "tag_name", new XCData(tag.Name)),
                !string.IsNullOrEmpty(tag.Description) ? new XElement(wpNs + "tag_description", new XCData(tag.Description)) : null));
        }

        var postId = 1;
        var commentId = 1;
        foreach (var item in contents)
        {
            var wpStatus = item.IsPrivate ? "private" : item.Status switch
            {
                ContentStatus.Published => "publish",
                ContentStatus.Pending => "pending",
                _ => "draft"
            };
            var postDate = (item.PublishedAt ?? item.CreatedAt).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            var itemEl = new XElement("item",
                new XElement("title", new XCData(item.Title)),
                new XElement("pubDate", (item.PublishedAt ?? item.CreatedAt).UtcDateTime.ToString("R")),
                new XElement(dcNs + "creator", new XCData(string.IsNullOrWhiteSpace(item.AuthorName) ? (settings?.AuthorDisplayName ?? "admin") : item.AuthorName)),
                new XElement(contentNs + "encoded", new XCData(md.Render(item.Markdown))),
                new XElement(excerptNs + "encoded", new XCData(item.Excerpt)),
                new XElement(wpNs + "post_id", postId++),
                new XElement(wpNs + "post_date", new XCData(postDate)),
                new XElement(wpNs + "post_date_gmt", new XCData(postDate)),
                new XElement(wpNs + "comment_status", item.AllowComments ? "open" : "closed"),
                new XElement(wpNs + "post_name", new XCData(item.Slug)),
                new XElement(wpNs + "status", new XCData(wpStatus)),
                new XElement(wpNs + "post_type", new XCData(item.Kind == ContentKind.Page ? "page" : "post")),
                new XElement(wpNs + "is_sticky", item.IsSticky ? 1 : 0));

            foreach (var cat in item.Categories)
            {
                itemEl.Add(new XElement("category",
                    new XAttribute("domain", "category"),
                    new XAttribute("nicename", cat.Slug),
                    new XCData(cat.Name)));
            }
            foreach (var tag in item.Tags)
            {
                itemEl.Add(new XElement("category",
                    new XAttribute("domain", "post_tag"),
                    new XAttribute("nicename", tag.Slug),
                    new XCData(tag.Name)));
            }
            foreach (var comment in commentsByContent[item.Id])
            {
                var cDate = comment.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                itemEl.Add(new XElement(wpNs + "comment",
                    new XElement(wpNs + "comment_id", commentId++),
                    new XElement(wpNs + "comment_author", new XCData(comment.AuthorName)),
                    new XElement(wpNs + "comment_author_url", comment.Url),
                    new XElement(wpNs + "comment_date", cDate),
                    new XElement(wpNs + "comment_date_gmt", cDate),
                    new XElement(wpNs + "comment_content", new XCData(comment.Markdown)),
                    new XElement(wpNs + "comment_approved", "1")));
            }
            channel.Add(itemEl);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "excerpt", excerptNs),
                new XAttribute(XNamespace.Xmlns + "content", contentNs),
                new XAttribute(XNamespace.Xmlns + "dc", dcNs),
                new XAttribute(XNamespace.Xmlns + "wp", wpNs),
                channel));

        await using var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true
        });
        await doc.SaveAsync(writer, ct);
        await writer.FlushAsync();
    }

    private static HashSet<string> ParseAllowedHosts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsSafeAttachmentUrl(string rawUrl, IReadOnlySet<string> allowedHosts, out Uri? safeUri, out string reason)
    {
        safeUri = null;
        reason = "";
        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            reason = "網址格式無效。";
            return false;
        }
        // SSRF 防護：重用 WebhookService 的私有／迴路／內部網段檢查。
        if (!WebhookService.IsAllowedWebhookUrl(uri.AbsoluteUri))
        {
            reason = "僅允許公開 HTTPS 網址（禁止本機或內網位址）。";
            return false;
        }
        if (allowedHosts.Count > 0 && !allowedHosts.Contains(uri.Host.ToLowerInvariant()))
        {
            reason = $"網域 {uri.Host} 不在允許的白名單內。";
            return false;
        }
        var ext = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".pdf" or ".mp3" or ".m4a" or ".mp4" or ".webm" or ".ogg" or ".wav"))
        {
            reason = $"不支援的附件副檔名（{ext}）。";
            return false;
        }
        safeUri = uri;
        return true;
    }

    /// <summary>讀入並解析 WXR；禁止 DTD 與外部實體，避免 XML 外部實體攻擊。</summary>
    private static async Task<XDocument> LoadAsync(Stream input, CancellationToken ct)
    {
        // 邊讀邊檢查上限：不可先把任意大小的輸入全部讀進記憶體再驗證。
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                throw new CmsException($"WXR 檔案上限為 {MaxBytes / 1024 / 1024} MB。");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (buffer.Length == 0) throw new CmsException("WXR 檔案是空的。");
        buffer.Position = 0;
        using var reader = XmlReader.Create(buffer, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            Async = true
        });
        try
        {
            return await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        }
        catch (XmlException error)
        {
            throw new CmsException($"WXR 檔案格式錯誤：{error.Message}");
        }
    }

    /// <summary>channel 下的 item 元素（文章、頁面、附件）。</summary>
    private static List<XElement> ItemsOf(XDocument document) =>
        document.Descendants().Where(x => x.Name.LocalName == "item").ToList();

    /// <summary>channel 下的分類與標籤宣告；WXR 各版本分別用 wp:category 與 wp:tag 元素。</summary>
    private static List<(bool IsTag, string Slug, string Name)> DeclaredTerms(XDocument document)
    {
        var terms = new List<(bool, string, string)>();
        foreach (var element in document.Descendants())
        {
            var (isTag, slugField, nameField) = element.Name.LocalName switch
            {
                "category" => (false, "category_nicename", "cat_name"),
                "tag" => (true, "tag_slug", "tag_name"),
                _ => (false, "", "")
            };
            if (slugField.Length == 0) continue;
            var slug = Text(element, slugField);
            var name = Text(element, nameField);
            if (slug.Length == 0 || name.Length == 0) continue;
            terms.Add((isTag, slug, name));
        }
        return terms;
    }

    /// <summary>代稱正規化；無法正規化時回傳 null，由呼叫端記錄警告並略過該項。</summary>
    private static string? NormalizeSlug(string slug, string fallback)
    {
        try { return ContentRules.NormalizeSlug(slug); }
        catch (CmsException)
        {
            if (fallback.Length == 0) return null;
            try { return ContentRules.NormalizeSlug(fallback); }
            catch (CmsException) { return null; }
        }
    }

    /// <summary>取出子元素文字；同名元素取第一個非空值。</summary>
    private static string Text(XElement parent, string localName)
    {
        foreach (var child in parent.Elements())
        {
            if (!child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase)) continue;
            var value = child.Value.Trim();
            if (value.Length > 0) return value;
        }
        return "";
    }

    /// <summary>依序嘗試多個元素名（WXR 各版本元素名不一致）。</summary>
    private static string TextAny(XElement parent, params string[] localNames)
    {
        foreach (var name in localNames)
        {
            var value = Text(parent, name);
            if (value.Length > 0) return value;
        }
        return "";
    }

    /// <summary>WordPress 日期格式為 yyyy-MM-dd HH:mm:ss（站台本地時間），此處視為 UTC。</summary>
    private static DateTimeOffset? ParseDate(string value)
    {
        if (value.Length == 0) return null;
        var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd" };
        if (DateTimeOffset.TryParseExact(value, formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;
        return null;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
