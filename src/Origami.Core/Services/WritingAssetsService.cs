namespace Origami.Core.Services;

/// <summary>
/// 內容範本與可重複片段：站點層級的 Markdown 骨架與文字片段。
/// 範本只在建立新內容時套用，片段由作者自行插入；兩者都不改動既有內容，也不載入程式碼。
/// </summary>
public sealed class WritingAssetsService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin)
{
    public sealed record TemplateView(Guid Id, string Name, ContentKind Kind, Guid? ContentTypeId, string Title, string Markdown, int Position)
    {
        public Guid Version { get; init; }
    }

    public sealed class TemplateEdit
    {
        public Guid? Id { get; set; }
        [Required, StringLength(Limits.TemplateName)] public string Name { get; set; } = "";
        public ContentKind Kind { get; set; } = ContentKind.Post;
        public Guid? ContentTypeId { get; set; }
        [StringLength(Limits.Title)] public string Title { get; set; } = "";
        [Required, StringLength(Limits.TemplateBody)] public string Markdown { get; set; } = "";
        public int Position { get; set; }
        public Guid Version { get; set; }
    }

    public sealed record SnippetView(Guid Id, string Name, string Markdown, int Position)
    {
        public Guid Version { get; init; }
    }

    public sealed class SnippetEdit
    {
        public Guid? Id { get; set; }
        [Required, StringLength(Limits.TemplateName)] public string Name { get; set; } = "";
        [Required, StringLength(Limits.SnippetBody)] public string Markdown { get; set; } = "";
        public int Position { get; set; }
        public Guid Version { get; set; }
    }

    public async Task<List<TemplateView>> ListTemplatesAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ContentTemplates.AsNoTracking().OrderBy(x => x.Position).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new TemplateView(x.Id, x.Name, x.Kind, x.ContentTypeId, x.Title, x.Markdown, x.Position) { Version = x.Version })
            .ToListAsync(ct);
    }

    public async Task<TemplateView> SaveTemplateAsync(TemplateEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ContentRules.Validate(input);
        if (input.Name.Trim().Length == 0) throw new CmsException("請輸入範本名稱。");
        if (input.Markdown.Trim().Length == 0) throw new CmsException("請輸入範本內容。");
        if (!Enum.IsDefined(input.Kind)) throw new CmsException("內容種類無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (input.ContentTypeId is { } typeId)
        {
            var typeKind = await db.ContentTypes.AsNoTracking().Where(x => x.Id == typeId).Select(x => (bool?)x.IsArchived).SingleOrDefaultAsync(ct);
            if (typeKind is null) throw new CmsException("內容類型不存在。");
            input.Kind = ContentKind.Post;
        }
        var version = Guid.NewGuid();
        ContentTemplate entity;
        if (input.Id is { } existingId)
        {
            entity = await db.ContentTemplates.SingleOrDefaultAsync(x => x.Id == existingId, ct) ?? throw new CmsException("範本不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        }
        else
        {
            entity = new ContentTemplate();
            db.ContentTemplates.Add(entity);
        }
        entity.Name = input.Name.Trim();
        entity.Kind = input.Kind;
        entity.ContentTypeId = input.ContentTypeId;
        entity.Title = (input.Title ?? "").Trim();
        entity.Markdown = input.Markdown.Trim();
        entity.Position = input.Position;
        entity.Version = version;
        await ContentRules.SaveAsync(db, ct);
        return new TemplateView(entity.Id, entity.Name, entity.Kind, entity.ContentTypeId, entity.Title, entity.Markdown, entity.Position) { Version = version };
    }

    public async Task DeleteTemplateAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.ContentTemplates.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("範本不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        db.ContentTemplates.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    public async Task<List<SnippetView>> ListSnippetsAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ContentSnippets.AsNoTracking().OrderBy(x => x.Position).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new SnippetView(x.Id, x.Name, x.Markdown, x.Position) { Version = x.Version })
            .ToListAsync(ct);
    }

    public async Task<SnippetView> SaveSnippetAsync(SnippetEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ContentRules.Validate(input);
        if (input.Name.Trim().Length == 0) throw new CmsException("請輸入片段名稱。");
        if (input.Markdown.Trim().Length == 0) throw new CmsException("請輸入片段內容。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var version = Guid.NewGuid();
        ContentSnippet entity;
        if (input.Id is { } existingId)
        {
            entity = await db.ContentSnippets.SingleOrDefaultAsync(x => x.Id == existingId, ct) ?? throw new CmsException("片段不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        }
        else
        {
            entity = new ContentSnippet();
            db.ContentSnippets.Add(entity);
        }
        entity.Name = input.Name.Trim();
        entity.Markdown = input.Markdown.Trim();
        entity.Position = input.Position;
        entity.Version = version;
        await ContentRules.SaveAsync(db, ct);
        return new SnippetView(entity.Id, entity.Name, entity.Markdown, entity.Position) { Version = version };
    }

    public async Task DeleteSnippetAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.ContentSnippets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("片段不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        db.ContentSnippets.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }
}
