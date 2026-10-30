namespace Origami.Core.Data;

public sealed class CmsDbContext(DbContextOptions<CmsDbContext> options, SiteContext? siteContext = null)
    : IdentityDbContext<AdminUser>(options)
{
    private const string HostPattern = "[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*";

    public Guid CurrentSiteId => siteContext?.SiteId ?? SiteDefaults.DefaultSiteId;
    public DbSet<CmsSite> Sites => Set<CmsSite>();
    public DbSet<SiteMembership> SiteMemberships => Set<SiteMembership>();
    public DbSet<SiteNavItem> SiteNavItems => Set<SiteNavItem>();
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<ContentComment> Comments => Set<ContentComment>();
    public DbSet<ContentRevision> ContentRevisions => Set<ContentRevision>();
    public DbSet<ContentView> ContentViews => Set<ContentView>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ContentType> ContentTypes => Set<ContentType>();
    public DbSet<CustomTaxonomy> CustomTaxonomies => Set<CustomTaxonomy>();
    public DbSet<CustomTaxonomyTerm> CustomTaxonomyTerms => Set<CustomTaxonomyTerm>();
    public DbSet<ContentCustomTerm> ContentCustomTerms => Set<ContentCustomTerm>();
    public DbSet<CustomField> CustomFields => Set<CustomField>();
    public DbSet<ContentFieldValue> ContentFieldValues => Set<ContentFieldValue>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<ContentSlugRedirect> SlugRedirects => Set<ContentSlugRedirect>();
    public DbSet<SiteSettings> Settings => Set<SiteSettings>();
    public DbSet<ContentEmbedding> ContentEmbeddings => Set<ContentEmbedding>();
    public DbSet<EmbeddingJob> EmbeddingJobs => Set<EmbeddingJob>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<WebhookDeliveryRecord> WebhookDeliveries => Set<WebhookDeliveryRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("vector");
        builder.Entity<AdminUser>(e =>
        {
            e.Property(x => x.Bio).HasMaxLength(Limits.UserBio).IsRequired();
            e.Property(x => x.WebsiteUrl).HasMaxLength(Limits.UserUrl).IsRequired();
            e.Property(x => x.AvatarUrl).HasMaxLength(Limits.UserUrl).IsRequired();
            e.Property(x => x.RecoveryCodeHashes).HasMaxLength(1024).IsRequired();
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
            e.Property(x => x.DisplayName).HasMaxLength(Limits.AuthorName).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_SiteMembership_Role", "\"Role\" IN (0, 1, 2, 3)");
                t.HasCheckConstraint("CK_SiteMembership_DisplayName", "\"DisplayName\" = btrim(\"DisplayName\")");
            });
        });
        builder.Entity<SiteNavItem>(e =>
        {
            ConfigureSiteEntity(e, nameof(SiteNavItem.Site));
            e.Property(x => x.Title).HasMaxLength(Limits.NavTitle).IsRequired();
            e.Property(x => x.Url).HasMaxLength(Limits.NavUrl).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            // 站內頁面項目：目標必須是本站的已發布頁面；頁面被永久刪除時連帶移除該導覽項目。
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SiteId, x.Position, x.Id });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_SiteNavItem_Title", "length(btrim(\"Title\")) > 0");
                t.HasCheckConstraint("CK_SiteNavItem_Target",
                    "(\"ContentId\" IS NOT NULL AND \"Url\" = '') OR (\"ContentId\" IS NULL AND \"Url\" <> '')");
            });
        });
        builder.Entity<ApiCredential>(e =>        {
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
            e.HasIndex(x => new { x.SiteId, x.AuthorSlug, x.Status });
            // 複合 FK 的 SiteId 不可清空；刪除前由服務明確清除 ParentId／CoverMediaId。
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => new { x.SiteId, x.ParentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Cover).WithMany().HasForeignKey(x => new { x.SiteId, x.CoverMediaId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            // 自訂內容類型以複合 FK 指向本站的類型宣告；類型封存不影響既有內容。
            e.HasOne(x => x.ContentType).WithMany().HasForeignKey(x => new { x.SiteId, x.ContentTypeId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SiteId, x.ContentTypeId, x.Status, x.PublishedAt, x.Id });
            e.HasMany(x => x.CustomTerms).WithOne().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            // 作者為全站帳號（不隨站點隔離）；帳號刪除時清空參照，署名快照 AuthorName 保留。
            e.Property(x => x.AuthorName).HasMaxLength(Limits.AuthorName).IsRequired();
            e.Property(x => x.AuthorSlug).HasMaxLength(Limits.Slug).IsRequired();
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.AuthorId);
            // 編輯占用鎖只是提示性心跳，不參與並發控制（並發仍由 Version 樂觀檢查把關）。
            e.Property(x => x.EditingByName).HasMaxLength(Limits.AuthorName).IsRequired();
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
                t.HasCheckConstraint("CK_Content_Status", "\"Status\" IN (0, 1, 2, 3)");
                // 草稿、回收桶與待審內容可為未發布；只有生效中的已發布狀態需要日期。
                t.HasCheckConstraint("CK_Content_PublishedAt", "\"Status\" IN (0, 2, 3) OR \"PublishedAt\" IS NOT NULL");
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
            e.Property(x => x.SnapshotJson).HasMaxLength(Limits.RevisionSnapshotMax).IsRequired();
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
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => new { x.SiteId, x.ParentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Category_Parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\""));
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
        builder.Entity<ContentType>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Name).HasMaxLength(Limits.ContentTypeName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.ContentTypeSlug).IsRequired();
            e.Property(x => x.Description).HasMaxLength(Limits.Description).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
            e.HasMany(x => x.Taxonomies).WithOne(x => x.ContentType)
                .HasForeignKey(x => new { x.SiteId, x.ContentTypeId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ContentType_Name", "length(btrim(\"Name\")) > 0");
                t.HasCheckConstraint("CK_ContentType_Slug", "length(btrim(\"Slug\")) > 0");
            });
        });
        builder.Entity<CustomTaxonomy>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Name).HasMaxLength(Limits.CustomTaxonomyName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Description).HasMaxLength(Limits.TaxonomyDescription).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.SiteId, x.ContentTypeId, x.Slug }).IsUnique();
            e.HasMany(x => x.Terms).WithOne(x => x.Taxonomy)
                .HasForeignKey(x => new { x.SiteId, x.TaxonomyId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_CustomTaxonomy_Name", "length(btrim(\"Name\")) > 0");
                t.HasCheckConstraint("CK_CustomTaxonomy_Slug", "length(btrim(\"Slug\")) > 0");
            });
        });
        builder.Entity<CustomTaxonomyTerm>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Name).HasMaxLength(Limits.CustomTaxonomyName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Description).HasMaxLength(Limits.TaxonomyDescription).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.SiteId, x.TaxonomyId, x.Slug }).IsUnique();
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => new { x.SiteId, x.ParentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_CustomTerm_Name", "length(btrim(\"Name\")) > 0");
                t.HasCheckConstraint("CK_CustomTerm_Slug", "length(btrim(\"Slug\")) > 0");
                t.HasCheckConstraint("CK_CustomTerm_Parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
            });
        });
        builder.Entity<ContentCustomTerm>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasIndex(x => new { x.SiteId, x.ContentId, x.TermId }).IsUnique();
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Term).WithMany().HasForeignKey(x => new { x.SiteId, x.TermId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<CustomField>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Name).HasMaxLength(Limits.CustomFieldName).IsRequired();
            e.Property(x => x.Key).HasMaxLength(Limits.CustomFieldName).IsRequired();
            e.Property(x => x.FieldType).HasMaxLength(20).IsRequired();
            e.Property(x => x.Options).HasMaxLength(Limits.CustomFieldOptions).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.SiteId, x.ContentTypeId, x.Key }).IsUnique();
            e.HasOne(x => x.ContentType).WithMany()
                .HasForeignKey(x => new { x.SiteId, x.ContentTypeId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_CustomField_Name", "length(btrim(\"Name\")) > 0");
                t.HasCheckConstraint("CK_CustomField_Key", "length(btrim(\"Key\")) > 0");
                t.HasCheckConstraint("CK_CustomField_Type",
                    "\"FieldType\" IN ('text', 'longtext', 'number', 'date', 'checkbox', 'url', 'select', 'media')");
            });
        });
        builder.Entity<ContentFieldValue>(e =>
        {
            ConfigureSiteEntity(e);
            e.Property(x => x.Value).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => new { x.SiteId, x.ContentId, x.FieldId }).IsUnique();
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Field).WithMany().HasForeignKey(x => new { x.SiteId, x.FieldId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
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
            e.ToTable(t => t.HasCheckConstraint("CK_MediaAsset_Kind", "\"Kind\" IN (0, 1, 2, 3)"));
        });
        builder.Entity<ContentSlugRedirect>(e =>
        {
            ConfigureSiteEntity(e);
            e.Property(x => x.OldSlug).HasMaxLength(Limits.Slug).IsRequired();
            e.HasIndex(x => new { x.SiteId, x.OldSlug }).IsUnique();
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ContentComment>(e =>
        {
            ConfigureSiteEntity(e, nameof(ContentComment.Site));
            e.HasAlternateKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.AuthorName).HasMaxLength(Limits.CommentAuthor).IsRequired();
            e.Property(x => x.Url).HasMaxLength(Limits.CommentUrl).IsRequired();
            e.Property(x => x.Markdown).HasMaxLength(Limits.CommentBody).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            // 評論隨內容刪除而移除；狀態與時間供審核佇列排序。
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => new { x.SiteId, x.ContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => new { x.SiteId, x.ParentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SiteId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.SiteId, x.ContentId, x.Status, x.CreatedAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Comment_Status", "\"Status\" IN (0, 1, 2, 3)");
                t.HasCheckConstraint("CK_Comment_Author", "length(btrim(\"AuthorName\")) > 0");
                t.HasCheckConstraint("CK_Comment_Body", "length(btrim(\"Markdown\")) > 0");
                t.HasCheckConstraint("CK_Comment_Parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
            });
        });
        builder.Entity<SiteSettings>(e =>
        {
            ConfigureSiteEntity(e);
            e.HasKey(x => new { x.SiteId, x.Id });
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.AuthorDisplayName).HasMaxLength(Limits.AuthorName).IsRequired();
            e.Property(x => x.PermalinkScheme).HasMaxLength(Limits.PermalinkScheme).IsRequired();
            e.Property(x => x.TimeZoneId).HasMaxLength(Limits.TimeZoneId).IsRequired();
            e.Property(x => x.DateFormat).HasMaxLength(Limits.DateFormat).IsRequired();
            e.Property(x => x.TimeFormat).HasMaxLength(Limits.TimeFormat).IsRequired();
            e.HasOne<Content>().WithMany().HasForeignKey(x => new { x.SiteId, x.HomePageContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Content>().WithMany().HasForeignKey(x => new { x.SiteId, x.PrivacyPageContentId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MediaAsset>().WithMany().HasForeignKey(x => new { x.SiteId, x.SiteIconMediaId })
                .HasPrincipalKey(x => new { x.SiteId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MediaAsset>().WithMany().HasForeignKey(x => new { x.SiteId, x.LogoMediaId })
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
        builder.Entity<AuditLog>(e =>
        {
            ConfigureSiteEntity(e);
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.UserName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasMaxLength(Limits.AuditAction).IsRequired();
            e.Property(x => x.Target).HasMaxLength(Limits.AuditDetail).IsRequired();
            e.HasIndex(x => new { x.SiteId, x.CreatedAt, x.Id });
        });
        builder.Entity<WebhookDeliveryRecord>(e =>
        {
            ConfigureSiteEntity(e);
            e.Property(x => x.Event).HasMaxLength(64).IsRequired();
            e.Property(x => x.TargetUrl).HasMaxLength(Limits.WebhookUrl).IsRequired();
            e.Property(x => x.PayloadJson).HasMaxLength(Limits.WebhookPayloadMax).IsRequired();
            e.Property(x => x.Signature).HasMaxLength(160).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(Limits.WebhookErrorMax).IsRequired();
            e.HasIndex(x => new { x.SiteId, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.SiteId, x.Status, x.NextAttemptAt });
        });

        ApplyChineseComments(builder);
    }

    private static readonly Dictionary<string, string> TableComments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AspNetUsers"] = "后台账号表：保存管理员/编辑者账号及登录安全信息。",
        ["AspNetRoles"] = "系统角色表：Identity 内置角色定义。",
        ["AspNetUserRoles"] = "用户角色关联表：Identity 用户与角色多对多关系。",
        ["AspNetUserClaims"] = "用户声明表：Identity 用户附加声明。",
        ["AspNetUserLogins"] = "外部登录表：Identity 第三方登录绑定。",
        ["AspNetUserTokens"] = "用户令牌表：Identity 令牌与会话相关数据。",
        ["AspNetRoleClaims"] = "角色声明表：Identity 角色附加声明。",
        ["Sites"] = "站点表：多站点租户的基础信息。",
        ["SiteMemberships"] = "站点成员表：用户在站点内的角色与署名。",
        ["SiteNavItems"] = "站点导航表：前台自定义导航项。",
        ["ApiCredentials"] = "API 凭据表：站点级访问密钥（仅存摘要）。",
        ["Contents"] = "内容主表：文章/页面及其发布状态。",
        ["ContentRevisions"] = "内容修订表：内容历史快照。",
        ["ContentViews"] = "内容浏览统计表：按天聚合的浏览次数。",
        ["Categories"] = "分类表：内容分类体系。",
        ["Tags"] = "标签表：内容标签体系。",
        ["ContentTypes"] = "内容类型表：自定义内容模型定义。",
        ["CustomTaxonomies"] = "自定义分类法表：挂载到内容类型的词汇集。",
        ["CustomTaxonomyTerms"] = "自定义词条表：自定义分类法下的具体词条。",
        ["ContentCustomTerms"] = "内容-自定义词条关联表。",
        ["CustomFields"] = "自定义字段定义表：内容类型可扩展字段。",
        ["ContentFieldValues"] = "内容自定义字段值表。",
        ["MediaAssets"] = "媒体资源表：图片/文档/音视频元数据。",
        ["SlugRedirects"] = "Slug 重定向表：旧路径到内容的永久跳转映射。",
        ["Settings"] = "站点设置表：站点配置与展示选项。",
        ["ContentEmbeddings"] = "语义向量表：内容切片与向量。",
        ["EmbeddingJobs"] = "向量生成任务表：嵌入计算队列。",
        ["Comments"] = "评论表：访客评论与审核状态。",
        ["AuditLogs"] = "审计日志表：关键管理操作记录。",
        ["WebhookDeliveries"] = "Webhook 投递记录表：出站事件投递状态与重试。",
        ["ContentCategories"] = "内容-分类关联表。",
        ["ContentTags"] = "内容-标签关联表。"
    };

    private static readonly Dictionary<string, string> CommonColumnComments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Id"] = "主键 ID。",
        ["SiteId"] = "所属站点 ID（租户隔离键）。",
        ["UserId"] = "用户 ID。",
        ["ContentId"] = "内容 ID。",
        ["ParentId"] = "父级对象 ID。",
        ["Name"] = "名称。",
        ["Slug"] = "URL 代称（站内唯一或局部唯一）。",
        ["Description"] = "描述说明。",
        ["Title"] = "标题。",
        ["Markdown"] = "Markdown 正文。",
        ["Status"] = "状态枚举值。",
        ["Kind"] = "类型枚举值。",
        ["CreatedAt"] = "创建时间。",
        ["UpdatedAt"] = "更新时间。",
        ["PublishedAt"] = "发布时间。",
        ["Version"] = "并发版本号（乐观并发控制）。",
        ["Url"] = "链接地址。",
        ["Position"] = "排序位置（值越小越靠前）。",
        ["Count"] = "计数值。",
        ["Date"] = "日期。",
        ["IsActive"] = "是否启用。",
        ["IsArchived"] = "是否归档。"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> ColumnCommentsByTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Contents"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Kind"] = "内容类型：文章或页面。",
                ["Excerpt"] = "内容摘要。",
                ["MetaDescription"] = "SEO 元描述。",
                ["NoIndex"] = "是否禁止搜索引擎索引。",
                ["IsSticky"] = "是否置顶（文章列表优先显示）。",
                ["AllowComments"] = "是否允许评论。",
                ["MenuOrder"] = "页面菜单排序权重。",
                ["PublishAt"] = "计划发布时间。",
                ["DeletedAt"] = "进入回收站时间。",
                ["PreviewToken"] = "预览令牌（发布前分享预览）。",
                ["IsProtected"] = "是否启用访问密码。",
                ["ProtectionHash"] = "访问密码哈希值。",
                ["IsPrivate"] = "是否私密（仅后台可见）。",
                ["ContentTypeId"] = "自定义内容类型 ID。",
                ["CoverMediaId"] = "封面媒体 ID。",
                ["AuthorId"] = "作者账号 ID。",
                ["AuthorName"] = "作者显示名快照。",
                ["AuthorSlug"] = "作者 URL 代称。",
                ["EditingById"] = "当前编辑锁占用者 ID。",
                ["EditingByName"] = "当前编辑锁占用者名称。",
                ["EditingAt"] = "编辑锁最近心跳时间。"
            },
            ["ContentRevisions"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SnapshotJson"] = "结构化快照 JSON（分类/标签/SEO/字段值等）。"
            },
            ["ContentViews"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Count"] = "当日浏览次数。"
            },
            ["MediaAssets"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["FileName"] = "存储文件名。",
                ["OriginalName"] = "原始上传文件名。",
                ["ContentType"] = "MIME 类型。",
                ["AltText"] = "替代文本（无障碍/SEO）。",
                ["Caption"] = "媒体说明文字。",
                ["Size"] = "文件大小（字节）。",
                ["Width"] = "宽度（像素）。",
                ["Height"] = "高度（像素）。",
                ["UploadedAt"] = "上传时间。"
            },
            ["Settings"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = "单例主键（固定为 1）。",
                ["PostsPerPage"] = "列表每页文章数。",
                ["FeedItems"] = "订阅源输出条目数。",
                ["FeedShowFullText"] = "订阅源是否输出全文。",
                ["AuthorDisplayName"] = "前台默认作者显示名。",
                ["HomePageContentId"] = "首页绑定内容 ID。",
                ["CustomNavigationEnabled"] = "是否启用受控导航。",
                ["CommentsEnabled"] = "是否启用评论系统。",
                ["CommentAutoApprove"] = "评论是否自动通过。",
                ["SiteIconMediaId"] = "站点图标媒体 ID。",
                ["LogoMediaId"] = "站点 Logo 媒体 ID。",
                ["ThemePreset"] = "主题预设名称。",
                ["AccentColor"] = "主题强调色。",
                ["ReadingWidth"] = "阅读宽度（字符列）。",
                ["ShowByline"] = "是否显示作者署名。",
                ["ShowCover"] = "是否显示封面图。",
                ["CardStyle"] = "卡片样式。",
                ["ColorScheme"] = "颜色模式。",
                ["FooterText"] = "页脚文本。",
                ["SocialGithubUrl"] = "GitHub 社交链接。",
                ["SocialXUrl"] = "X/Twitter 社交链接。",
                ["SocialMastodonUrl"] = "Mastodon 社交链接。",
                ["CommentBlockedWords"] = "评论敏感词列表。",
                ["CommentCloseDays"] = "发布后自动关闭评论天数。",
                ["MaintenanceMode"] = "是否启用维护模式。",
                ["MaintenanceMessage"] = "维护模式提示语。",
                ["WebhookUrl"] = "Webhook 接收地址。",
                ["WebhookSecret"] = "Webhook 签名密钥。",
                ["PrivacyPageContentId"] = "隐私政策页面 ID。",
                ["EmailNotificationsEnabled"] = "是否发送邮件通知。",
                ["PermalinkScheme"] = "固定链接方案。",
                ["TimeZoneId"] = "站点时区。",
                ["DateFormat"] = "日期显示格式。",
                ["TimeFormat"] = "时间显示格式。"
            },
            ["ApiCredentials"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SecretHash"] = "API 密钥哈希（不存明文）。",
                ["SecurityStamp"] = "安全戳（失效控制）。",
                ["Scope"] = "权限范围（只读/读写）。",
                ["ExpiresAt"] = "过期时间。",
                ["RevokedAt"] = "撤销时间。"
            },
            ["Comments"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["AuthorName"] = "评论作者显示名。",
                ["Url"] = "评论作者网址。",
                ["Markdown"] = "评论内容（Markdown）。",
                ["IsAdminReply"] = "是否官方回复。"
            },
            ["ContentEmbeddings"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["ChunkIndex"] = "切片序号。",
                ["ChunkText"] = "切片文本。",
                ["Embedding"] = "向量数据。"
            },
            ["EmbeddingJobs"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["EnqueuedAt"] = "入队时间。",
                ["Attempts"] = "已重试次数。",
                ["NextAttemptAt"] = "下次重试时间。"
            },
            ["WebhookDeliveries"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Event"] = "事件名称。",
                ["TargetUrl"] = "投递目标地址。",
                ["PayloadJson"] = "投递负载 JSON。",
                ["Signature"] = "请求签名。",
                ["Attempts"] = "投递尝试次数。",
                ["LastStatusCode"] = "最近一次响应状态码。",
                ["LastError"] = "最近一次错误信息。",
                ["NextAttemptAt"] = "下次重试时间。",
                ["DeliveredAt"] = "最终投递成功时间。"
            },
            ["AuditLogs"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["UserName"] = "操作者用户名快照。",
                ["Action"] = "操作动作。",
                ["Target"] = "操作目标。"
            },
            ["AspNetUsers"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SingletonKey"] = "全局单例管理员标记。",
                ["IsDisabled"] = "账号是否禁用。",
                ["TwoFactorSecret"] = "两步验证密钥。",
                ["RecoveryCodeHashes"] = "恢复码哈希集合。",
                ["Bio"] = "用户简介。",
                ["WebsiteUrl"] = "个人网站地址。",
                ["AvatarUrl"] = "头像地址。"
            }
        };

    private static void ApplyChineseComments(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
                continue;

            entityType.SetComment(GetTableComment(tableName));

            foreach (var property in entityType.GetProperties())
                property.SetComment(GetColumnComment(tableName, property.Name));
        }
    }

    private static string GetTableComment(string tableName) =>
        TableComments.TryGetValue(tableName, out var comment)
            ? comment
            : $"业务表：{tableName}。";

    private static string GetColumnComment(string tableName, string columnName)
    {
        if (ColumnCommentsByTable.TryGetValue(tableName, out var tableColumns) &&
            tableColumns.TryGetValue(columnName, out var tableComment))
            return tableComment;

        if (CommonColumnComments.TryGetValue(columnName, out var commonComment))
            return commonComment;

        return $"业务字段：{columnName}。";
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
