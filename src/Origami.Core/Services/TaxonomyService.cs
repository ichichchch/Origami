namespace Origami.Core.Services;

public sealed class TaxonomyService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin)
{
    public async Task<PagedResult<TaxonomyItem>> ListAsync(TaxonomyKind kind, int page = 1, int pageSize = Limits.MaxPageSize, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ValidateKind(kind);
        var offset = ContentRules.Offset(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (kind == TaxonomyKind.Category)
        {
            var total = await db.Categories.CountAsync(ct);
            var items = await db.Categories.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new TaxonomyItem(x.Id, x.Name, x.Slug, x.Version) { Description = x.Description }).ToListAsync(ct);
            return new(items, total, page, pageSize);
        }
        else
        {
            var total = await db.Tags.CountAsync(ct);
            var items = await db.Tags.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new TaxonomyItem(x.Id, x.Name, x.Slug, x.Version) { Description = x.Description }).ToListAsync(ct);
            return new(items, total, page, pageSize);
        }
    }

    public async Task<TaxonomyEdit> SaveAsync(TaxonomyKind kind, TaxonomyEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ValidateKind(kind);
        ContentRules.Validate(input);
        var slug = ContentRules.NormalizeSlug(input.Id is null && string.IsNullOrWhiteSpace(input.Slug) ? input.Name : input.Slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        var version = Guid.NewGuid();
        Guid id;
        if (kind == TaxonomyKind.Category)
        {
            var entity = input.Id is { } existingId
                ? await db.Categories.SingleOrDefaultAsync(x => x.Id == existingId, ct) ?? throw new CmsException("分類不存在。")
                : new Category();
            if (input.Id is null) db.Categories.Add(entity);
            else db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
            entity.Name = input.Name.Trim();
            entity.Slug = slug;
            entity.Description = (input.Description ?? "").Trim();
            entity.Version = version;
            id = entity.Id;
        }
        else
        {
            var entity = input.Id is { } existingId
                ? await db.Tags.SingleOrDefaultAsync(x => x.Id == existingId, ct) ?? throw new CmsException("標籤不存在。")
                : new Tag();
            if (input.Id is null) db.Tags.Add(entity);
            else db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
            entity.Name = input.Name.Trim();
            entity.Slug = slug;
            entity.Description = (input.Description ?? "").Trim();
            entity.Version = version;
            id = entity.Id;
        }
        await ContentRules.SaveAsync(db, ct);
        input.Id = id;
        input.Slug = slug;
        input.Version = version;
        return input;
    }

    public async Task DeleteAsync(TaxonomyKind kind, Guid id, Guid version, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ValidateKind(kind);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (kind == TaxonomyKind.Category)
        {
            var entity = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("分類不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = version;
            db.Categories.Remove(entity);
        }
        else
        {
            var entity = await db.Tags.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("標籤不存在。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = version;
            db.Tags.Remove(entity);
        }
        await ContentRules.SaveAsync(db, ct);
    }

    private static void ValidateKind(TaxonomyKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new CmsException("分類類型無效。");
    }
}
