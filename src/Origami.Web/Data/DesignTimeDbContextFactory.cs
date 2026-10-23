namespace Origami.Web.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(string[] args)
    {
        // Schema generation needs no running server. Applying migrations requires explicit external configuration.
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Cms")
            ?? "Host=127.0.0.1;Port=1;Database=design_only;Username=design_only;Timeout=1";
        return new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(connection, o => o.UseVector()).Options);
    }
}
