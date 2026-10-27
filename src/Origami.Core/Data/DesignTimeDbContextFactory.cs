namespace Origami.Core.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(string[] args)
    {
        // 產生結構不需執行中的伺服器。套用遷移則需要明確的外部設定。
        var connection = Environment.GetEnvironmentVariable("CMS")
            ?? "Host=127.0.0.1;Port=1;Database=design_only;Username=design_only;Timeout=1";
        return new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(connection, o => o.UseVector()).Options);
    }
}
