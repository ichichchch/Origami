namespace Origami.Core.Services;

/// <summary>
/// 自訂內容類型與自訂分類法：以宣告方式管理，不載入任何程式碼、不對外提供擴充執行點。
/// 類型、分類法與詞彙皆依站點隔離；內容仍以既有 <see cref="Content"/> 儲存。
/// </summary>
public sealed class ContentTypeService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock)
{
    /// <summary>內容類型的公開路由前綴；固定前綴讓 Blazor 路由無需動態註冊。</summary>
    public const string PublicPrefix = "t";

    public sealed record TypeView(Guid Id, string Name, string Slug, string Description,
        bool SupportsCategories, bool SupportsTags, bool SupportsHierarchy, bool IsArchived, int ContentCount)
    {
        /// <summary>並發版本；刪除時須攜帶。</summary>
        public Guid Version { get; init; }
    }

    public sealed record TaxonomyView(Guid Id, Guid ContentTypeId, string Name, string Slug, string Description,
        bool IsHierarchical, bool IsArchived, int TermCount)
    {
        /// <summary>並發版本；刪除時須攜帶。</summary>
        public Guid Version { get; init; }
    }

    public sealed record TermView(Guid Id, Guid TaxonomyId, string Name, string Slug, string Description, Guid? ParentId)
    {
        /// <summary>並發版本；刪除時須攜帶。</summary>
        public Guid Version { get; init; }
    }

    public sealed class TypeEdit
    {
        public Guid? Id { get; set; }
        [Required, StringLength(Limits.ContentTypeName)] public string Name { get; set; } = "";
        [Required, StringLength(Limits.ContentTypeSlug)] public string Slug { get; set; } = "";
        [StringLength(Limits.Description)] public string Description { get; set; } = "";
        public bool SupportsCategories { get; set; }
        public bool SupportsTags { get; set; }
        public bool SupportsHierarchy { get; set; }
        public bool IsArchived { get; set; }
        public Guid Version { get; set; }
    }

    public sealed class TaxonomyEdit
    {
        public Guid? Id { get; set; }
        public Guid ContentTypeId { get; set; }
        [Required, StringLength(Limits.CustomTaxonomyName)] public string Name { get; set; } = "";
        [Required, StringLength(Limits.Slug)] public string Slug { get; set; } = "";
        [StringLength(Limits.TaxonomyDescription)] public string? Description { get; set; }
        public bool IsHierarchical { get; set; }
        public bool IsArchived { get; set; }
        public Guid Version { get; set; }
    }

    public sealed class TermEdit
    {
        public Guid? Id { get; set; }
        public Guid TaxonomyId { get; set; }
        [Required, StringLength(Limits.CustomTaxonomyName)] public string Name { get; set; } = "";
        [Required, StringLength(Limits.Slug)] public string Slug { get; set; } = "";
        [StringLength(Limits.TaxonomyDescription)] public string? Description { get; set; }
        public Guid? ParentId { get; set; }
        public Guid Version { get; set; }
    }

    /// <summary>後台可見的內容類型清單（含已封存者）。</summary>
    public async Task<List<TypeView>> ListAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.Contents.AsNoTracking().Where(x => x.ContentTypeId != null)
            .GroupBy(x => x.ContentTypeId!.Value).Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return await db.ContentTypes.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new TypeView(x.Id, x.Name, x.Slug, x.Description, x.SupportsCategories, x.SupportsTags,
                x.SupportsHierarchy, x.IsArchived, counts.ContainsKey(x.Id) ? counts[x.Id] : 0) { Version = x.Version })
            .ToListAsync(ct);
    }

    /// <summary>公開可見的內容類型；已封存者不對外提供。</summary>
    public async Task<List<TypeView>> ListPublicAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.Contents.AsNoTracking()
            .Where(x => x.ContentTypeId != null && x.Status == ContentStatus.Published && !x.IsPrivate)
            .GroupBy(x => x.ContentTypeId!.Value).Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return await db.ContentTypes.AsNoTracking().Where(x => !x.IsArchived)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new TypeView(x.Id, x.Name, x.Slug, x.Description, x.SupportsCategories, x.SupportsTags,
                x.SupportsHierarchy, x.IsArchived, counts.ContainsKey(x.Id) ? counts[x.Id] : 0) { Version = x.Version })
            .ToListAsync(ct);
    }

    /// <summary>以路由代稱解析內容類型；不存在或已封存時回傳 null。</summary>
    public async Task<TypeView?> ResolvePublicAsync(string? slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var normalized = ContentRules.NormalizeSlug(slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ContentTypes.AsNoTracking().Where(x => x.Slug == normalized && !x.IsArchived)
            .Select(x => new TypeView(x.Id, x.Name, x.Slug, x.Description, x.SupportsCategories, x.SupportsTags,
                x.SupportsHierarchy, x.IsArchived, 0) { Version = x.Version })
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>後台以識別取得內容類型。</summary>
    public async Task<TypeView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ContentTypes.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new TypeView(x.Id, x.Name, x.Slug, x.Description, x.SupportsCategories, x.SupportsTags,
                x.SupportsHierarchy, x.IsArchived, 0) { Version = x.Version })
            .SingleOrDefaultAsync(ct);
    }

    public async Task<TypeView> SaveAsync(TypeEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ContentRules.Validate(input);
        var slug = ContentRules.NormalizeSlug(input.Slug);
        if (slug.Length > Limits.ContentTypeSlug) throw new CmsException($"路由代稱須為 1–{Limits.ContentTypeSlug} 字。");
        if (slug == PublicPrefix) throw new CmsException("此代稱為系統保留字，請改用其他代稱。");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.ContentTypes.AsNoTracking().AnyAsync(x => x.Slug == slug
            && (!input.Id.HasValue || x.Id != input.Id.Value), ct))
            throw new CmsException("路由代稱已存在，請使用其他代稱。");
        var version = Guid.NewGuid();
        ContentType entity;
        if (input.Id is { } existingId)
        {
            entity = await db.ContentTypes.SingleOrDefaultAsync(x => x.Id == existingId, ct)
                ?? throw new CmsException("內容類型不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        }
        else
        {
            entity = new ContentType { CreatedAt = clock.GetUtcNow() };
            db.ContentTypes.Add(entity);
        }
        entity.Name = input.Name.Trim();
        entity.Slug = slug;
        entity.Description = (input.Description ?? "").Trim();
        entity.SupportsCategories = input.SupportsCategories;
        entity.SupportsTags = input.SupportsTags;
        entity.SupportsHierarchy = input.SupportsHierarchy;
        entity.IsArchived = input.IsArchived;
        entity.Version = version;
        await ContentRules.SaveAsync(db, ct);
        input.Id = entity.Id;
        input.Slug = slug;
        input.Version = version;
        var count = await db.Contents.AsNoTracking().CountAsync(x => x.ContentTypeId == entity.Id, ct);
        return new(entity.Id, entity.Name, entity.Slug, entity.Description, entity.SupportsCategories,
            entity.SupportsTags, entity.SupportsHierarchy, entity.IsArchived, count) { Version = entity.Version };
    }

    /// <summary>刪除內容類型；仍有關聯內容時一律拒絕，應改為封存。</summary>
    public async Task DeleteAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.ContentTypes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("內容類型不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        if (await db.Contents.AsNoTracking().AnyAsync(x => x.ContentTypeId == id, ct))
            throw new CmsException("此類型仍有內容，請先移出或刪除內容；也可以改為封存。");
        db.ContentTypes.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    public async Task<List<TaxonomyView>> ListTaxonomiesAsync(Guid contentTypeId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.CustomTaxonomyTerms.AsNoTracking()
            .GroupBy(x => x.TaxonomyId).Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return await db.CustomTaxonomies.AsNoTracking().Where(x => x.ContentTypeId == contentTypeId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new TaxonomyView(x.Id, x.ContentTypeId, x.Name, x.Slug, x.Description, x.IsHierarchical,
                x.IsArchived, counts.ContainsKey(x.Id) ? counts[x.Id] : 0) { Version = x.Version })
            .ToListAsync(ct);
    }

    public async Task<TaxonomyView> SaveTaxonomyAsync(TaxonomyEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ContentRules.Validate(input);
        var slug = ContentRules.NormalizeSlug(input.Slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.ContentTypes.AsNoTracking().AnyAsync(x => x.Id == input.ContentTypeId, ct))
            throw new CmsException("內容類型不存在。");
        if (await db.CustomTaxonomies.AsNoTracking().AnyAsync(x => x.ContentTypeId == input.ContentTypeId
            && x.Slug == slug && (!input.Id.HasValue || x.Id != input.Id.Value), ct))
            throw new CmsException("代稱已存在，請使用其他代稱。");
        var version = Guid.NewGuid();
        CustomTaxonomy entity;
        if (input.Id is { } existingId)
        {
            entity = await db.CustomTaxonomies.SingleOrDefaultAsync(x => x.Id == existingId, ct)
                ?? throw new CmsException("自訂分類法不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
            if (entity.ContentTypeId != input.ContentTypeId) throw new CmsException("不可將分類法改掛到其他內容類型。");
        }
        else
        {
            entity = new CustomTaxonomy();
            db.CustomTaxonomies.Add(entity);
        }
        entity.ContentTypeId = input.ContentTypeId;
        entity.Name = input.Name.Trim();
        entity.Slug = slug;
        entity.Description = (input.Description ?? "").Trim();
        entity.IsHierarchical = input.IsHierarchical;
        entity.IsArchived = input.IsArchived;
        entity.Version = version;
        await ContentRules.SaveAsync(db, ct);
        input.Id = entity.Id;
        input.Slug = slug;
        input.Version = version;
        var count = await db.CustomTaxonomyTerms.AsNoTracking().CountAsync(x => x.TaxonomyId == entity.Id, ct);
        return new(entity.Id, entity.ContentTypeId, entity.Name, entity.Slug, entity.Description,
            entity.IsHierarchical, entity.IsArchived, count) { Version = entity.Version };
    }

    public async Task DeleteTaxonomyAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.CustomTaxonomies.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CmsException("自訂分類法不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        db.CustomTaxonomies.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    public async Task<List<TermView>> ListTermsAsync(Guid taxonomyId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.CustomTaxonomyTerms.AsNoTracking().Where(x => x.TaxonomyId == taxonomyId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new TermView(x.Id, x.TaxonomyId, x.Name, x.Slug, x.Description, x.ParentId) { Version = x.Version })
            .ToListAsync(ct);
    }

    public async Task<TermView> SaveTermAsync(TermEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ContentRules.Validate(input);
        var slug = ContentRules.NormalizeSlug(input.Slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        var taxonomy = await db.CustomTaxonomies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.TaxonomyId, ct)
            ?? throw new CmsException("自訂分類法不存在。");
        if (await db.CustomTaxonomyTerms.AsNoTracking().AnyAsync(x => x.TaxonomyId == input.TaxonomyId
            && x.Slug == slug && (!input.Id.HasValue || x.Id != input.Id.Value), ct))
            throw new CmsException("代稱已存在，請使用其他代稱。");
        if (input.ParentId is { } parentId)
        {
            if (!taxonomy.IsHierarchical) throw new CmsException("此分類法未啟用階層，詞彙不可指定上層。");
            var parent = await db.CustomTaxonomyTerms.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == parentId && x.TaxonomyId == input.TaxonomyId, ct)
                ?? throw new CmsException("上層詞彙不存在。");
            if (input.Id is { } selfId && parentId == selfId) throw new CmsException("詞彙不可將自己設為上層。");
        }
        var version = Guid.NewGuid();
        CustomTaxonomyTerm entity;
        if (input.Id is { } existingId)
        {
            entity = await db.CustomTaxonomyTerms.SingleOrDefaultAsync(x => x.Id == existingId, ct)
                ?? throw new CmsException("詞彙不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
            if (entity.TaxonomyId != input.TaxonomyId) throw new CmsException("不可將詞彙改掛到其他分類法。");
        }
        else
        {
            entity = new CustomTaxonomyTerm();
            db.CustomTaxonomyTerms.Add(entity);
        }
        entity.TaxonomyId = input.TaxonomyId;
        entity.Name = input.Name.Trim();
        entity.Slug = slug;
        entity.Description = (input.Description ?? "").Trim();
        entity.ParentId = input.ParentId;
        entity.Version = version;
        await ContentRules.SaveAsync(db, ct);
        input.Id = entity.Id;
        input.Slug = slug;
        input.Version = version;
        return new(entity.Id, entity.TaxonomyId, entity.Name, entity.Slug, entity.Description, entity.ParentId)
        { Version = entity.Version };
    }

    public async Task DeleteTermAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.CustomTaxonomyTerms.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CmsException("詞彙不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        if (await db.CustomTaxonomyTerms.AsNoTracking().AnyAsync(x => x.ParentId == id, ct))
            throw new CmsException("此詞彙仍有上層或下層詞彙，請先移除關聯。");
        db.CustomTaxonomyTerms.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>某個內容類型底下的分類法與詞彙，供內容編輯器選擇。</summary>
    public sealed record TaxonomyTermsView(Guid TaxonomyId, string TaxonomyName, List<TermView> Terms);

    public sealed record FieldView(Guid Id, Guid ContentTypeId, string Name, string Key, string FieldType,
        bool IsRequired, int Position, bool IsArchived, string Options = "")
    {
        /// <summary>並發版本；刪除時須攜帶。</summary>
        public Guid Version { get; init; }
    }

    public sealed class FieldEdit
    {
        public Guid? Id { get; set; }
        public Guid ContentTypeId { get; set; }
        [Required, StringLength(Limits.CustomFieldName)] public string Name { get; set; } = "";
        [Required, StringLength(Limits.CustomFieldName)] public string Key { get; set; } = "";
        [Required, StringLength(20)] public string FieldType { get; set; } = CustomFieldTypes.Text;
        [StringLength(Limits.CustomFieldOptions)] public string Options { get; set; } = "";
        public bool IsRequired { get; set; }
        public int Position { get; set; }
        public bool IsArchived { get; set; }
        public Guid Version { get; set; }
    }

    /// <summary>內容類型底下的自訂欄位宣告（受控擴充點）。</summary>
    public async Task<List<FieldView>> ListFieldsAsync(Guid contentTypeId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.CustomFields.AsNoTracking().Where(x => x.ContentTypeId == contentTypeId)
            .OrderBy(x => x.Position).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new FieldView(x.Id, x.ContentTypeId, x.Name, x.Key, x.FieldType, x.IsRequired,
                x.Position, x.IsArchived, x.Options) { Version = x.Version })
            .ToListAsync(ct);
    }

    public async Task<FieldView> SaveFieldAsync(FieldEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ContentRules.Validate(input);
        if (!CustomFieldTypes.IsValid(input.FieldType)) throw new CmsException("欄位種類無效。");
        var normalizedType = input.FieldType.ToLowerInvariant();
        var options = normalizedType == CustomFieldTypes.Select ? (input.Options ?? "").Trim() : "";
        if (normalizedType == CustomFieldTypes.Select && CustomFieldRules.ParseOptions(options).Count == 0)
            throw new CmsException("下拉選單欄位須提供至少一個選項（以逗號分隔）。");
        var key = ContentRules.NormalizeSlug(input.Key);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.ContentTypes.AsNoTracking().AnyAsync(x => x.Id == input.ContentTypeId, ct))
            throw new CmsException("內容類型不存在。");
        if (await db.CustomFields.AsNoTracking().AnyAsync(x => x.ContentTypeId == input.ContentTypeId
            && x.Key == key && (!input.Id.HasValue || x.Id != input.Id.Value), ct))
            throw new CmsException("欄位鍵值已存在，請改用其他鍵值。");
        var version = Guid.NewGuid();
        CustomField entity;
        if (input.Id is { } existingId)
        {
            entity = await db.CustomFields.SingleOrDefaultAsync(x => x.Id == existingId, ct)
                ?? throw new CmsException("自訂欄位不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
            if (entity.ContentTypeId != input.ContentTypeId) throw new CmsException("不可將欄位改掛到其他內容類型。");
        }
        else
        {
            entity = new CustomField();
            db.CustomFields.Add(entity);
        }
        entity.ContentTypeId = input.ContentTypeId;
        entity.Name = input.Name.Trim();
        entity.Key = key;
        entity.FieldType = normalizedType;
        entity.Options = options;
        entity.IsRequired = input.IsRequired;
        entity.Position = input.Position;
        entity.IsArchived = input.IsArchived;
        entity.Version = version;
        await ContentRules.SaveAsync(db, ct);
        input.Id = entity.Id;
        input.Key = key;
        input.Version = version;
        return new(entity.Id, entity.ContentTypeId, entity.Name, entity.Key, entity.FieldType, entity.IsRequired,
            entity.Position, entity.IsArchived, entity.Options) { Version = entity.Version };
    }

    public async Task DeleteFieldAsync(Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.CustomFields.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CmsException("自訂欄位不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        db.CustomFields.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    /// <summary>某個內容類型底下的分類法與詞彙，供內容編輯器選擇。</summary>
    public async Task<List<TaxonomyTermsView>> TermsForContentTypeAsync(
        Guid contentTypeId, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Reader, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var taxonomies = await db.CustomTaxonomies.AsNoTracking()
            .Where(x => x.ContentTypeId == contentTypeId && !x.IsArchived)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var result = new List<TaxonomyTermsView>();
        foreach (var taxonomy in taxonomies)
        {
            var terms = await db.CustomTaxonomyTerms.AsNoTracking().Where(x => x.TaxonomyId == taxonomy.Id)
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new TermView(x.Id, x.TaxonomyId, x.Name, x.Slug, x.Description, x.ParentId) { Version = x.Version })
                .ToListAsync(ct);
            result.Add(new(taxonomy.Id, taxonomy.Name, terms));
        }
        return result;
    }
}
