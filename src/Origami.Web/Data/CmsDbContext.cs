using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Origami.Web.Models;

namespace Origami.Web.Data;

public sealed class CmsDbContext(DbContextOptions<CmsDbContext> options) : IdentityDbContext<AdminUser>(options)
{
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
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
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.Kind, x.Status, x.PublishedAt, x.Id });
            e.HasMany(x => x.Categories).WithMany(x => x.Contents).UsingEntity("ContentCategories");
            e.HasMany(x => x.Tags).WithMany(x => x.Contents).UsingEntity("ContentTags");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Content_Kind", "\"Kind\" IN (0, 1)");
                t.HasCheckConstraint("CK_Content_Status", "\"Status\" IN (0, 1)");
                t.HasCheckConstraint("CK_Content_PublishedAt", "\"Status\" = 0 OR \"PublishedAt\" IS NOT NULL");
                t.HasCheckConstraint("CK_Content_Title", "length(btrim(\"Title\")) > 0");
                t.HasCheckConstraint("CK_Content_Slug", "length(\"Slug\") > 0");
            });
        });
        builder.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.TaxonomyName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Slug).IsUnique();
        });
        builder.Entity<Tag>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.TaxonomyName).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(Limits.Slug).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Slug).IsUnique();
        });
        builder.Entity<MediaAsset>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(80).IsRequired();
            e.Property(x => x.OriginalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.FileName).IsUnique();
            e.HasIndex(x => new { x.UploadedAt, x.Id });
        });
        builder.Entity<SiteSettings>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.ToTable(t => t.HasCheckConstraint("CK_Settings_Singleton", "\"Id\" = 1"));
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
