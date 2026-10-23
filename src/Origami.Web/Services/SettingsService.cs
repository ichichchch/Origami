using Microsoft.EntityFrameworkCore;
using Origami.Web.Data;
using Origami.Web.Models;

namespace Origami.Web.Services;

public sealed class SettingsService(IDbContextFactory<CmsDbContext> factory, AdminAccess admin)
{
    public async Task<SiteSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Settings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
    }

    public async Task<SiteSettings> SaveAsync(SiteSettings input, CancellationToken ct = default)
    {
        await admin.RequireAsync(ct);
        ContentRules.Validate(input);
        if (input.Id != 1) throw new CmsException("站點設定識別無效。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Settings.SingleAsync(x => x.Id == 1, ct);
        db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        entity.Name = input.Name.Trim();
        entity.Description = input.Description?.Trim() ?? "";
        entity.Version = Guid.NewGuid();
        await ContentRules.SaveAsync(db, ct);
        input.Version = entity.Version;
        return input;
    }
}
