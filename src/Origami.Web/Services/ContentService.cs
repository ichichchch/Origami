using Microsoft.EntityFrameworkCore;
using Origami.Web.Data;
using Origami.Web.Models;

namespace Origami.Web.Services;

public sealed class ContentService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin, TimeProvider clock)
{
    public async Task<PagedResult<ContentSummary>> ListAdminAsync(ContentKind kind, int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        var offset = ContentRules.Offset(page, pageSize);
        if (!Enum.IsDefined(kind)) throw new CmsException("內容類型無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Contents.AsNoTracking().Where(x => x.Kind == kind);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize).Select(x => new ContentSummary(x.Id, x.Kind, x.Title, x.Slug, x.Status, x.UpdatedAt, x.PublishedAt, x.Version)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<ContentEdit> GetEditAsync(Guid id, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().AsSplitQuery().Where(x => x.Id == id).Select(x => new ContentEdit
        {
            Id = x.Id, Kind = x.Kind, Title = x.Title, Slug = x.Slug, Markdown = x.Markdown,
            Status = x.Status, Version = x.Version,
            CategoryIds = x.Categories.Select(c => c.Id).ToList(), TagIds = x.Tags.Select(t => t.Id).ToList()
        }).SingleOrDefaultAsync(ct) ?? throw new CmsException("內容不存在。");
    }

    public async Task<ContentEdit> SaveAsync(ContentEdit input, CancellationToken ct = default)
    {
        // Convenience wrapper. The kind of an existing content always comes from the stored row;
        // a new content states its kind explicitly and it is validated before use.
        return input.Id is null
            ? await CreateAsync(input.Kind, input, ct)
            : await UpdateAsync(input, ct);
    }

    public async Task<ContentEdit> CreateAsync(ContentKind kind, ContentEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (input.Id is not null) throw new CmsException("既有內容請改用更新。");
        return await SaveCoreAsync(kind, input, null, ct);
    }

    public async Task<ContentEdit> UpdateAsync(ContentEdit input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (input.Id is not { } id) throw new CmsException("缺少內容識別。");
        return await SaveCoreAsync(null, input, id, ct);
    }

    /// <summary>
    /// The stored row decides the content kind. A caller may request a kind only when creating
    /// content, and the value is validated against the persisted type on every update.
    /// </summary>
    private async Task<ContentEdit> SaveCoreAsync(ContentKind? requestedKind, ContentEdit input, Guid? id, CancellationToken ct)
    {
        ContentRules.Validate(input);
        if (!Enum.IsDefined(input.Status)) throw new CmsException("內容狀態無效。");
        if (requestedKind is { } kind && !Enum.IsDefined(kind)) throw new CmsException("內容類型無效。");
        if (input.CategoryIds is null || input.TagIds is null || input.CategoryIds.Count > Limits.MaxTaxonomiesPerKind || input.TagIds.Count > Limits.MaxTaxonomiesPerKind)
            throw new CmsException($"每篇文章最多 {Limits.MaxTaxonomiesPerKind} 個分類與標籤。");
        var slug = ContentRules.NormalizeSlug(id is null && string.IsNullOrWhiteSpace(input.Slug) ? input.Title : input.Slug);
        await using var db = await factory.CreateDbContextAsync(ct);
        Content entity;
        var now = clock.GetUtcNow();
        if (id is { } existingId)
        {
            entity = await db.Contents.Include(x => x.Categories).Include(x => x.Tags).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == existingId, ct)
                ?? throw new CmsException("內容已被刪除。");
            // The persisted kind is authoritative; a submitted change is refused, never applied.
            if (input.Kind != entity.Kind) throw new CmsException("不可變更既有內容類型。");
            if (entity.Version != input.Version) throw new CmsException("資料已被另一個分頁更新，請重新載入後再編輯。");
            db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        }
        else
        {
            entity = new Content { Kind = requestedKind!.Value, CreatedAt = now };
            db.Contents.Add(entity);
        }
        var isPage = entity.Kind == ContentKind.Page;
        if (isPage && (input.CategoryIds.Count != 0 || input.TagIds.Count != 0))
            throw new CmsException("獨立頁面不可指派文章分類或標籤。");
        var categoryIds = input.CategoryIds.Distinct().ToList();
        var tagIds = input.TagIds.Distinct().ToList();
        var categories = await db.Categories.Where(x => categoryIds.Contains(x.Id)).ToListAsync(ct);
        var tags = await db.Tags.Where(x => tagIds.Contains(x.Id)).ToListAsync(ct);
        if (categories.Count != categoryIds.Count || tags.Count != tagIds.Count) throw new CmsException("所選分類或標籤不存在，請重新載入。");
        entity.Title = input.Title.Trim();
        entity.Slug = slug;
        entity.Markdown = input.Markdown ?? "";
        entity.Status = input.Status;
        if (entity.Status == ContentStatus.Published) entity.PublishedAt ??= now;
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        entity.Categories = categories;
        entity.Tags = tags;
        // The embedding lifecycle rides in the same transaction as the content change:
        // a published state queues re-embedding, any other state removes vectors immediately
        // so withdrawn or draft content can never surface through semantic search.
        if (entity.Status == ContentStatus.Published)
        {
            if (!await db.EmbeddingJobs.AnyAsync(x => x.ContentId == entity.Id, ct))
                db.EmbeddingJobs.Add(new EmbeddingJob { ContentId = entity.Id, EnqueuedAt = now, NextAttemptAt = now });
        }
        else
        {
            db.EmbeddingJobs.RemoveRange(await db.EmbeddingJobs.Where(x => x.ContentId == entity.Id).ToListAsync(ct));
            db.ContentEmbeddings.RemoveRange(await db.ContentEmbeddings.Where(x => x.ContentId == entity.Id).ToListAsync(ct));
        }
        // EF wraps the content update and both join-table updates in one transaction.
        await ContentRules.SaveAsync(db, ct);
        input.Id = entity.Id;
        input.Kind = entity.Kind;
        input.Slug = entity.Slug;
        input.Version = entity.Version;
        return input;
    }

    public async Task DeleteAsync(Guid id, Guid version, bool confirmed, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        if (!confirmed) throw new CmsException("請先確認永久刪除。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Contents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new CmsException("內容不存在。");
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        db.Remove(entity);
        await ContentRules.SaveAsync(db, ct);
    }

    public async Task<PagedResult<ContentSummary>> ListPublishedAsync(int page = 1, int pageSize = Limits.PageSize, CancellationToken ct = default)
    {
        var offset = ContentRules.Offset(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Post && x.Status == ContentStatus.Published);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Skip(offset).Take(pageSize)
            .Select(x => new ContentSummary(x.Id, x.Kind, x.Title, x.Slug, x.Status, x.UpdatedAt, x.PublishedAt, x.Version)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<PublicContent?> GetPublishedAsync(ContentKind kind, string slug, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().AsSplitQuery().Where(x => x.Kind == kind && x.Status == ContentStatus.Published && x.Slug == slug)
            .Select(x => new PublicContent(x.Id, x.Kind, x.Title, x.Slug, x.Markdown, x.PublishedAt,
                x.Categories.OrderBy(c => c.Name).Select(c => c.Name).ToList(), x.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList())).SingleOrDefaultAsync(ct);
    }

    public async Task<List<NavigationItem>> NavigationAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Contents.AsNoTracking().Where(x => x.Kind == ContentKind.Page && x.Status == ContentStatus.Published)
            .OrderBy(x => x.Title).ThenBy(x => x.Id).Select(x => new NavigationItem(x.Title, x.Slug)).ToListAsync(ct);
    }
}
