namespace Origami.Core.Data;

public sealed class CmsDbContext(DbContextOptions<CmsDbContext> options) : IdentityDbContext<AdminUser>(options)
{
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
            e.HasIndex(x => x.SingletonKey).IsUnique();
            e.ToTable("AspNetUsers", t => t.HasCheckConstraint("CK_Admin_Singleton", "\"SingletonKey\" = 1"));
        });
        builder.Entity<Content>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(Limits.Title).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Markdown).HasMaxLength(Limits.Body).IsRequired();
            e.Property(x => x.Excerpt).HasMaxLength(Limits.Excerpt).IsRequired();
            e.Property(x => x.ProtectionHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.Kind, x.Status, x.PublishedAt, x.Id });
            e.HasIndex(x => x.PreviewToken).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ParentId);
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Categories).WithMany(x => x.Contents).UsingEntity("ContentCategories");
            e.HasMany(x => x.Tags).WithMany(x => x.Contents).UsingEntity("ContentTags");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Content_Kind", "\"Kind\" IN (0, 1)");
                t.HasCheckConstraint("CK_Content_Status", "\"Status\" IN (0, 1, 2)");
                // Drafts and trash may be unpublished; only the live published state requires a date.
                t.HasCheckConstraint("CK_Content_PublishedAt", "\"Status\" IN (0, 2) OR \"PublishedAt\" IS NOT NULL");
                // A schedule is only meaningful on a draft; the trash timestamp only in the trash.
                t.HasCheckConstraint("CK_Content_PublishAt", "\"Status\" = 0 OR \"PublishAt\" IS NULL");
                t.HasCheckConstraint("CK_Content_DeletedAt", "(\"Status\" = 2) = (\"DeletedAt\" IS NOT NULL)");
                t.HasCheckConstraint("CK_Content_Title", "length(btrim(\"Title\")) > 0");
                t.HasCheckConstraint("CK_Content_Slug", "length(\"Slug\") > 0");
                // A page cannot protect and privatize at once, nor be its own parent.
                t.HasCheckConstraint("CK_Content_Private", "NOT (\"IsProtected\" AND \"IsPrivate\")");
                t.HasCheckConstraint("CK_Content_Parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
            });
        });
        builder.Entity<ContentRevision>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(Limits.Title).IsRequired();
            e.Property(x => x.Markdown).HasMaxLength(Limits.Body).IsRequired();
            e.Property(x => x.Excerpt).HasMaxLength(Limits.Excerpt).IsRequired();
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ContentId, x.CreatedAt });
        });
        builder.Entity<ContentView>(e =>
        {
            e.HasKey(x => new { x.ContentId, x.Date });
            e.HasOne(x => x.Content).WithMany(x => x.Views).HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.TaxonomyName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Description).HasMaxLength(Limits.TaxonomyDescription).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Slug).IsUnique();
        });
        builder.Entity<Tag>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.TaxonomyName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Description).HasMaxLength(Limits.TaxonomyDescription).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Slug).IsUnique();
        });
        builder.Entity<MediaAsset>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(80).IsRequired();
            e.Property(x => x.OriginalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(40).IsRequired();
            e.Property(x => x.AltText).HasMaxLength(Limits.AltText).IsRequired();
            e.HasIndex(x => x.FileName).IsUnique();
            e.HasIndex(x => new { x.UploadedAt, x.Id });
        });
        builder.Entity<ContentSlugRedirect>(e =>
        {
            e.Property(x => x.OldSlug).HasMaxLength(Limits.Slug).IsRequired();
            e.HasIndex(x => x.OldSlug).IsUnique();
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<SiteSettings>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Content>().WithMany().HasForeignKey(x => x.HomePageContentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.SiteIconMediaId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Settings_Singleton", "\"Id\" = 1");
                t.HasCheckConstraint("CK_Settings_PostsPerPage",
                    "\"PostsPerPage\" IS NULL OR \"PostsPerPage\" BETWEEN 1 AND " + Limits.MaxPageSize);
                t.HasCheckConstraint("CK_Settings_FeedItems",
                    "\"FeedItems\" IS NULL OR \"FeedItems\" BETWEEN 1 AND " + Limits.MaxFeedItems);
            });
            e.HasData(new SiteSettings { Id = 1, Version = new Guid("134d979d-707b-40b1-bd78-95d96033fd84") });
        });
        builder.Entity<ContentEmbedding>(e =>
        {
            e.HasKey(x => new { x.ContentId, x.ChunkIndex });
            e.Property(x => x.ChunkText).HasMaxLength(Limits.EmbeddingChunkMax).IsRequired();
            e.Property(x => x.Embedding).HasColumnType("vector(" + Limits.EmbeddingDimensions + ")");
            e.HasOne<Content>().WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
            // The ANN index keeps similarity ordering exact enough for search while avoiding full scans.
            e.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });
        builder.Entity<EmbeddingJob>(e =>
        {
            e.HasOne<Content>().WithMany().HasForeignKey(x => x.ContentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ContentId).IsUnique();
        });
    }
}
