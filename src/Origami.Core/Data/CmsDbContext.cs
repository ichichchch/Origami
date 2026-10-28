namespace Origami.Core.Data;

public sealed class CmsDbContext(DbContextOptions<CmsDbContext> options, SiteContext? siteContext = null)
    : IdentityDbContext<AdminUser>(options)
{
    private const string HostPattern = "[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*";

    public Guid CurrentSiteId => siteContext?.SiteId ?? SiteDefaults.DefaultSiteId;
    public DbSet<CmsSite> Sites => Set<CmsSite>();
    public DbSet<SiteMembership> SiteMemberships => Set<SiteMembership>();
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<ContentRevision> ContentRevisions => Set<ContentRevision>();
    public DbSet<ContentView> ContentViews => Set<ContentView>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<ContentSlugRedirect> SlugRedirects => Set<ContentSlugRedirect>();
    public DbSet<SiteSettings> Settings => Set<SiteSettings>();
    public DbSet<ContentEmbedding> ContentEmbeddings => Set<ContentEmbedding>();
    public DbSet<EmbeddingJob> EmbeddingJobs => Set<EmbeddingJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("vector");
        builder.Entity<AdminUser>(e =>
        {
            e.HasIndex(x => x.SingletonKey).IsUnique().HasFilter("\"SingletonKey\" IS NOT NULL");
            e.ToTable("AspNetUsers", t => t.HasCheckConstraint("CK_Admin_Singleton", "\"SingletonKey\" IS NULL OR \"SingletonKey\" = 1"));
        });
        builder.Entity<CmsSite>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.SiteName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.SiteSlug).IsRequired().UseCollation("C")
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            e.Property(x => x.HostName).HasMaxLength(Limits.HostName).UseCollation("C");
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.HostName).IsUnique().HasFilter("\"HostName\" IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Site_Name", "length(btrim(\"Name\")) > 0");
                t.HasCheckConstraint("CK_Site_Slug", "length(btrim(\"Slug\")) > 0");
                t.HasCheckConstraint("CK_Site_HostName", "\"HostName\" IS NULL OR \"HostName\" ~ '^" + HostPattern + "$'");
                t.HasCheckConstraint("CK_Site_DefaultHost", "\"HostName\" IS NOT NULL OR \"Id\" = '11111111-1111-1111-1111-111111111111'::uuid");
                t.HasCheckConstraint("CK_Site_DefaultActive", "\"Id\" <> '11111111-1111-1111-1111-111111111111'::uuid OR \"IsActive\"");
            });
            e.HasData(new CmsSite
            {
                Id = SiteDefaults.DefaultSiteId, Name = "Origami", Slug = "default", HostName = null,
                IsActive = true, Version = new Guid("134d979d-707b-40b1-bd78-95d96033fd85"),
                CreatedAt = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)
            });
        });
        builder.Entity<SiteMembership>(e =>
        {
            ConfigureSiteEntity(e, nameof(SiteMembership.Site));
            e.HasKey(x => new { x.SiteId, x.UserId });
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_SiteMembership_Role", "\"Role\" IN (0, 1, 2)"));
        });
        builder.Entity<ApiCredential>(e =>
        {
            ConfigureSiteEntity(e, nameof(ApiCredential.Site));
            e.Property(x => x.Name).HasMaxLength(Limits.CredentialName).IsRequired();
            e.Property(x => x.SecretHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.SecurityStamp).HasMaxLength(256).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SiteId, x.UserId, x.CreatedAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ApiCredential_Name", "length(btrim(\"Name\")) > 0");
                t.HasCheckConstraint("CK_ApiCredential_SecretHash", "\"SecretHash\" ~ '^[0-9a-fA-F]{64}$'");
                t.HasCheckConstraint("CK_ApiCredential_SecurityStamp", "length(\"SecurityStamp\") > 0");
                t.HasCheckConstraint("CK_ApiCredential_Scope", "\"Scope\" IN (0, 1)");
                t.HasCheckConstraint("CK_ApiCredential_ExpiresAt", "\"ExpiresAt\" > \"CreatedAt\"");
            });
        });
        builder.Entity<Content>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Title).HasMaxLength(Limits.Title).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Markdown).HasMaxLength(Limits.Body).IsRequired();
            e.Property(x => x.Excerpt).HasMaxLength(Limits.Excerpt).IsRequired();
            e.Property(x => x.ProtectionHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Kind, x.Status, x.PublishedAt, x.Id });
            e.HasIndex(x => x.PreviewToken).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            // 複合 FK 的 SiteId 不可清空；刪除前由服務明確清除 ParentId／CoverMediaId。
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => new { x.SiteId, x.ParentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Cover).WithMany().HasForeignKey(x => new { x.SiteId, x.CoverMediaId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Categories).WithMany(x => x.Contents).UsingEntity<Dictionary<string, object>>(
                "ContentCategories",
                j => j.HasOne<Category>().WithMany().HasForeignKey("SiteId", "CategoriesId")
                    .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade),
                j => j.HasOne<Content>().WithMany().HasForeignKey("SiteId", "ContentsId")
                    .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    ConfigureSiteEntity(j);
                    j.HasKey("SiteId", "CategoriesId", "ContentsId");
                });
            e.HasMany(x => x.Tags).WithMany(x => x.Contents).UsingEntity<Dictionary<string, object>>(
                "ContentTags",
                j => j.HasOne<Tag>().WithMany().HasForeignKey("SiteId", "TagsId")
                    .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade),
                j => j.HasOne<Content>().WithMany().HasForeignKey("SiteId", "ContentsId")
                    .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    ConfigureSiteEntity(j);
                    j.HasKey("SiteId", "ContentsId", "TagsId");
                });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Content_Kind", "\"Kind\" IN (0, 1)");
                t.HasCheckConstraint("CK_Content_Status", "\"Status\" IN (0, 1, 2)");
                // 草稿與回收桶內容可為未發布；只有生效中的已發布狀態需要日期。
                t.HasCheckConstraint("CK_Content_PublishedAt", "\"Status\" IN (0, 2) OR \"PublishedAt\" IS NOT NULL");
                // 排程只在草稿上有意義；回收桶時間戳只在回收桶中。
                t.HasCheckConstraint("CK_Content_PublishAt", "\"Status\" = 0 OR \"PublishAt\" IS NULL");
                t.HasCheckConstraint("CK_Content_DeletedAt", "(\"Status\" = 2) = (\"DeletedAt\" IS NOT NULL)");
                t.HasCheckConstraint("CK_Content_Title", "length(btrim(\"Title\")) > 0");
                t.HasCheckConstraint("CK_Content_Slug", "length(\"Slug\") > 0");
                // 頁面不能同時密碼保護又私密，也不能成為自己的父頁面。
                t.HasCheckConstraint("CK_Content_Private", "NOT (\"IsProtected\" AND \"IsPrivate\")");
                t.HasCheckConstraint("CK_Content_Parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
            });
        });
        builder.Entity<ContentRevision>(e =>
        {
            ConfigureSiteEntity(e);
            e.Property(x => x.Title).HasMaxLength(Limits.Title).IsRequired();
            e.Property(x => x.Markdown).HasMaxLength(Limits.Body).IsRequired();
            e.Property(x => x.Excerpt).HasMaxLength(Limits.Excerpt).IsRequired();
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SiteId, x.ContentId, x.CreatedAt });
        });
        builder.Entity<ContentView>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasKey(x => new { x.ContentId, x.Date });
            e.HasOne(x => x.Content).WithMany(x => x.Views).HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Category>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Name).HasMaxLength(Limits.TaxonomyName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Description).HasMaxLength(Limits.TaxonomyDescription).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
        });
        builder.Entity<Tag>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Name).HasMaxLength(Limits.TaxonomyName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Description).HasMaxLength(Limits.TaxonomyDescription).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
        });
        builder.Entity<MediaAsset>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.FileName).HasMaxLength(80).IsRequired();
            e.Property(x => x.OriginalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(40).IsRequired();
            e.Property(x => x.AltText).HasMaxLength(Limits.AltText).IsRequired();
            e.Property(x => x.Caption).HasMaxLength(Limits.Caption).IsRequired();
            e.HasIndex(x => new { x.SiteId, x.FileName }).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.UploadedAt, x.Id });
        });
        builder.Entity<ContentSlugRedirect>(e =>
        {
            ConfigureSiteEntity(e);
            e.Property(x => x.OldSlug).HasMaxLength(Limits.Slug).IsRequired();
            e.HasIndex(x => new { x.SiteId, x.OldSlug }).IsUnique();
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<SiteSettings>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.AuthorDisplayName).HasMaxLength(Limits.AuthorName).IsRequired();
            e.HasOne<Content>().WithMany().HasForeignKey(x => new { x.SiteId, x.HomePageContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MediaAsset>().WithMany().HasForeignKey(x => new { x.SiteId, x.SiteIconMediaId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Settings_Singleton", "\"Id\" = 1");
                t.HasCheckConstraint("CK_Settings_PostsPerPage",
                    "\"PostsPerPage\" IS NULL OR \"PostsPerPage\" BETWEEN 1 AND " + Limits.MaxPageSize);
                t.HasCheckConstraint("CK_Settings_FeedItems",
                    "\"FeedItems\" IS NULL OR \"FeedItems\" BETWEEN 1 AND " + Limits.MaxFeedItems);
            });
            e.HasData(new SiteSettings { SiteId = SiteDefaults.DefaultSiteId, Id = 1, Version = new Guid("134d979d-707b-40b1-bd78-95d96033fd84") });
        });
        builder.Entity<ContentEmbedding>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasKey(x => new { x.ContentId, x.ChunkIndex });
            e.Property(x => x.ChunkText).HasMaxLength(Limits.EmbeddingChunkMax).IsRequired();
            e.Property(x => x.Embedding).HasColumnType("vector(" + Limits.EmbeddingDimensions + ")");
            e.HasOne<Content>().WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            // ANN 索引讓相似度排序對搜尋而言足夠精確，同時避免全表掃描。
            e.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });
        builder.Entity<EmbeddingJob>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasOne<Content>().WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SiteId, x.ContentId }).IsUnique();
        });
    }

    private void ConfigureSiteEntity<TEntity>(EntityTypeBuilder<TEntity> entity, string? siteNavigation = null)
        where TEntity : class
    {
        // 不在模型建立時讀取 SiteId 值；EF 將此 context 成員參數化，跨站共用模型仍安全。
        entity.HasQueryFilter(x => EF.Property<Guid>(x, "SiteId") == CurrentSiteId);
        // 即使離線 attach 偽造原始值，UPDATE／DELETE 也只會命中當前站點。
        entity.Property<Guid>("SiteId").IsConcurrencyToken().ValueGeneratedNever();
        entity.HasOne<CmsSite>(siteNavigation).WithMany().HasForeignKey("SiteId").OnDelete(DeleteBehavior.Restrict);
    }

    public override int SaveChanges() => SaveChanges(true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareSiteChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PrepareSiteChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareSiteChanges()
    {
        var currentSite = CurrentSiteId;
        if (currentSite == Guid.Empty)
            throw new InvalidOperationException("儲存前必須先設定目前站點。");

        // 先檢查既有列再蓋寫 Added 的輸入；也涵蓋停用 AutoDetectChanges 的呼叫端。
        var autoDetectChanges = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var entries = ChangeTracker.Entries().ToArray();
            foreach (var entry in entries.Where(x => x.Metadata.FindProperty("SiteId") != null && x.State != EntityState.Added))
                ValidateSite(entry, currentSite);

            // 先蓋寫主體，再蓋寫共用型別 join；關聯 fixup 也會同步其複合 FK。
            foreach (var entry in entries.Where(x => x.State == EntityState.Added && x.Metadata.FindProperty("SiteId") != null)
                         .OrderBy(x => x.Metadata.IsPropertyBag))
                entry.Property("SiteId").CurrentValue = currentSite;
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }

        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().ToArray())
        {
            if (entry.Metadata.FindProperty("SiteId") != null)
            {
                if (entry.State == EntityState.Added)
                {
                    // DetectChanges 可因 skip navigation 變動而新增 join 列。
                    entry.Property("SiteId").CurrentValue = currentSite;
                }
                else
                {
                    ValidateSite(entry, currentSite);
                }
            }

            if (entry.Entity is CmsSite site)
            {
                if (site.Id == SiteDefaults.DefaultSiteId && (entry.State == EntityState.Deleted || !site.IsActive))
                    throw new InvalidOperationException("預設站點不可刪除或停用。");
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    if (site.HostName != null)
                    {
                        site.HostName = new IdnMapping().GetAscii(site.HostName.Trim().TrimEnd('.')).ToLowerInvariant();
                        if (site.HostName.Length > Limits.HostName || !Regex.IsMatch(site.HostName, "\\A" + HostPattern + "\\z"))
                            throw new ValidationException("主機名稱格式無效，且不得包含通訊埠。");
                    }
                    else if (site.Id != SiteDefaults.DefaultSiteId)
                    {
                        throw new ValidationException("只有預設站點可以不登錄主機名稱。");
                    }
                }
            }
        }
        ChangeTracker.DetectChanges();
        // 防止關聯 fixup 把剛剛 stamp 的站點改回不可信的導覽值。
        foreach (var entry in ChangeTracker.Entries().Where(x => x.Metadata.FindProperty("SiteId") != null))
            ValidateSite(entry, currentSite);
    }

    private static void ValidateSite(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Guid currentSite)
    {
        var site = entry.Property("SiteId");
        if (!Equals(site.CurrentValue, currentSite) ||
            (entry.State != EntityState.Added && !Equals(site.OriginalValue, currentSite)))
            throw new UnauthorizedAccessException("資料必須屬於目前站點，禁止跨站移動資料。");
    }
}
