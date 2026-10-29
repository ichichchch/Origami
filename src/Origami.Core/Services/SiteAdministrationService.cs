namespace Origami.Core.Services;

public sealed class SiteAdministrationService(
    IDbContextFactory<CmsDbContext> factory,
    AdminAccess admin,
    UserManager<AdminUser> userManager,
    TimeProvider timeProvider)
{
    private const int MaximumSites = 200;
    private const int MaximumMembers = 200;
    private const int MaximumUserName = 256;

    public async Task<SiteSummary?> ResolveHostAsync(string host, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string normalized;
        try { normalized = NormalizeHost(host); }
        catch (CmsException) { return null; }
        await using var db = await factory.CreateDbContextAsync(ct);
        var match = await db.Sites.AsNoTracking().Where(x => x.HostName == normalized)
            .Select(x => new SiteSummary(x.Id, x.Name, x.Slug, x.HostName, x.IsActive, x.Version))
            .SingleOrDefaultAsync(ct);
        // 已登錄但停用的網域不得退回預設站點；未登錄網域也不得退回，
        // 否則任何指向伺服器的網域都會服務預設站內容。僅本機開發
        // （localhost／127.0.0.1）在預設站尚未登錄網域時以預設站服務。
        if (match is not null) return match.IsActive ? match : null;
        if (normalized is not ("localhost" or "127.0.0.1")) return null;
        return await db.Sites.AsNoTracking()
            .Where(x => x.Id == SiteDefaults.DefaultSiteId && x.HostName == null && x.IsActive)
            .Select(x => new SiteSummary(x.Id, x.Name, x.Slug, x.HostName, x.IsActive, x.Version))
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>只接受完整主機名稱；不接受 URL、通訊埠、萬用字元或代理標頭。</summary>
    public static string NormalizeHost(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 1024 || host.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
            host.IndexOfAny(['/', '\\', '@', '*', '?', '#', '%']) >= 0)
            throw new CmsException("請輸入不含協定、路徑與通訊埠的完整主機名稱。");

        // 第一批採 DNS 網域／子網域；IPv4／localhost 供本機開發，IPv6 literal 不列入網域登錄。
        if (host.Contains(':') || host.Contains('[') || host.Contains(']'))
            throw new CmsException("主機名稱不得包含通訊埠或 IPv6 位址，請使用網域名稱。");
        if (host.EndsWith('.')) host = host[..^1];
        string result;
        try { result = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(host).ToLowerInvariant(); }
        catch (ArgumentException) { throw new CmsException("主機名稱格式無效。"); }
        if (result.Length is 0 or > Limits.HostName ||
            Uri.CheckHostName(result) is not (UriHostNameType.Dns or UriHostNameType.IPv4) ||
            result.Split('.').Any(label => !IsDnsLabel(label)))
            throw new CmsException("主機名稱格式無效。");
        return result;
    }

    public Task<SiteAccessInfo> GetAccessAsync(CancellationToken ct = default) => admin.GetCurrentAccessAsync(ct);

    public async Task<IReadOnlyList<SiteSummary>> ListSitesAsync(CancellationToken ct = default)
    {
        var session = await admin.GetValidatedUserAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Sites.AsNoTracking();
        if (session.CredentialScope is not null)
        {
            // PAT 只代表綁定站點，不因擁有者的全站權限暴露其他站點。
            query = query.Where(x => x.Id == session.SiteId && x.IsActive);
        }
        else if (session.User.SingletonKey != 1)
        {
            query = query.Where(x => x.IsActive && db.SiteMemberships.IgnoreQueryFilters()
                .Any(m => m.SiteId == x.Id && m.UserId == session.User.Id &&
                    (m.Role == SiteRole.Reader || m.Role == SiteRole.Editor || m.Role == SiteRole.Administrator)));
        }
        var sites = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(MaximumSites + 1)
            .Select(x => new SiteSummary(x.Id, x.Name, x.Slug, x.HostName, x.IsActive, x.Version)).ToListAsync(ct);
        if (sites.Count > MaximumSites) throw new CmsException("站點超過 200 筆，無法完整列出。");
        return sites;
    }

    public async Task<SiteSummary> CreateSiteAsync(SiteCreateInput input, CancellationToken ct = default)
    {
        var creator = await admin.RequireNetworkAdminAsync(ct);
        ArgumentNullException.ThrowIfNull(input);
        var name = ValidateName(input.Name, Limits.SiteName, "站點名稱");
        var slug = (input.Slug ?? "").Trim().ToLowerInvariant();
        if (!IsDnsLabel(slug) || slug.Length > Limits.SiteSlug)
            throw new CmsException("站點代稱須為 1–63 個 ASCII 英數字或連字號，開頭與結尾須為英數字。");
        var host = NormalizeHost(input.HostName);
        var site = new CmsSite { Name = name, Slug = slug, HostName = host, CreatedAt = timeProvider.GetUtcNow() };
        return await WriteSafelyAsync(async () =>
        {
            await using var template = await factory.CreateDbContextAsync(ct);
            // 複用工廠的完整 provider／攔截器設定，只給新 context 專屬的租戶範圍。
            // 不切換請求的 SiteContext，也不略過 SaveChanges 的站點 guard。
            var options = (DbContextOptions<CmsDbContext>)template.GetService<IDbContextOptions>();
            await using var db = new CmsDbContext(options, new SiteContext { SiteId = site.Id });
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockSiteAsync(db, SiteDefaults.DefaultSiteId, ct);
            await admin.RequireNetworkAdminAsync(ct);
            if (await db.Sites.CountAsync(ct) >= MaximumSites)
                throw new CmsException("最多只能建立 200 個站點。");
            db.Sites.Add(site);
            db.Settings.Add(new SiteSettings { SiteId = site.Id, Id = 1, Name = name });
            db.SiteMemberships.Add(new SiteMembership
            {
                SiteId = site.Id, UserId = creator.Id, Role = SiteRole.Administrator
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Summary(site);
        });
    }

    public async Task<SiteSummary> UpdateSiteAsync(Guid id, SiteUpdateInput input, CancellationToken ct = default)
    {
        await admin.RequireNetworkAdminAsync(ct);
        ArgumentNullException.ThrowIfNull(input);
        var name = ValidateName(input.Name, Limits.SiteName, "站點名稱");
        if (id == SiteDefaults.DefaultSiteId && !input.IsActive)
            throw new CmsException("預設站點不可停用。");
        var host = input.HostName is null && id == SiteDefaults.DefaultSiteId ? null : NormalizeHost(input.HostName!);
        return await WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var site = await LockSiteAsync(db, id, ct);
            await admin.RequireNetworkAdminAsync(ct);
            if (site.Version != input.Version) throw VersionConflict();
            site.Name = name;
            site.HostName = host;
            site.IsActive = input.IsActive;
            site.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Summary(site);
        });
    }

    public async Task<PagedResult<UserSummary>> ListUsersAsync(int page = 1, string? search = null, CancellationToken ct = default)
    {
        await admin.RequireNetworkAdminAsync(ct);
        var offset = ContentRules.Offset(page, Limits.SitePageSize);
        if (search?.Length > MaximumUserName) throw new CmsException("搜尋文字不可超過 256 字。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + ContentRules.EscapeLike(search.Trim()) + "%";
            query = query.Where(x => x.UserName != null && EF.Functions.ILike(x.UserName, pattern));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.NormalizedUserName).ThenBy(x => x.Id).Skip(offset).Take(Limits.SitePageSize)
            .Select(x => new UserSummary(x.Id, x.UserName ?? "", x.IsDisabled, x.SingletonKey == 1) { TwoFactorEnabled = x.TwoFactorEnabled }).ToListAsync(ct);
        return new(items, total, page, Limits.SitePageSize);
    }

    public async Task<UserSummary> CreateUserAsync(UserCreateInput input, CancellationToken ct = default)
    {
        await admin.RequireNetworkAdminAsync(ct);
        ArgumentNullException.ThrowIfNull(input);
        var name = ValidateName(input.UserName, MaximumUserName, "使用者名稱");
        var password = input.Password;
        // 防止主機誤用 Identity 預設較弱政策；實際建立仍通過既有 UserManager 的所有驗證器。
        if (string.IsNullOrEmpty(password) || password.Length is < 8 or > 1024 ||
            password.Distinct().Count() < 4 || !password.Any(char.IsAsciiDigit) ||
            !password.Any(char.IsAsciiLetterLower) || !password.Any(char.IsAsciiLetterUpper) ||
            !password.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new CmsException("密碼須為 8–1024 字，包含大小寫英文字母、數字、符號與至少 4 種不同字元。");
        return await WriteSafelyAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            // 電子郵件供密碼重設與通知使用；可留空，但沒有郵件的帳號無法自助重設密碼。
            var email = (input.Email ?? "").Trim();
            if (email.Length > 0)
            {
                if (email.Length > 256 || !email.Contains('@') || email.Any(char.IsControl))
                    throw new CmsException("電子郵件格式不正確。");
            }
            var user = new AdminUser { UserName = name, SingletonKey = null, IsDisabled = false, Email = email };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(x => x.Code == "DuplicateUserName"))
                    throw new CmsException("使用者名稱已存在。");
                throw new CmsException("無法建立帳號，請檢查使用者名稱及密碼規則。");
            }
            return new UserSummary(user.Id, user.UserName ?? "", user.IsDisabled, false);
        });
    }

    public async Task SetUserDisabledAsync(string id, bool disabled, CancellationToken ct = default)
    {
        var actor = await admin.RequireNetworkAdminAsync(ct);
        ValidateUserId(id);
        await WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // 與成員操作同樣先鎖站點；停用會改變所有站點的有效管理員計數。
            // 固定排序且有上限，避免不同站點交錯鎖定造成死結。
            var sites = await db.Sites.FromSqlRaw("SELECT * FROM \"Sites\" ORDER BY \"Id\" LIMIT 201 FOR UPDATE")
                .AsNoTracking().ToListAsync(ct);
            if (sites.Count > MaximumSites) throw new CmsException("站點超過 200 筆，請先處理站點上限。");
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw new CmsException("使用者不存在。");
            await admin.RequireNetworkAdminAsync(ct);
            if (disabled && (user.SingletonKey == 1 || user.Id == actor.Id))
                throw new CmsException("不可停用全站管理者或自己的帳號。");
            if (user.IsDisabled == disabled) return true;
            if (disabled)
            {
                var removesLastAdmin = await db.SiteMemberships.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(m => m.UserId == id && m.Role == SiteRole.Administrator &&
                        !db.SiteMemberships.IgnoreQueryFilters().Any(other => other.SiteId == m.SiteId &&
                            other.UserId != id && other.Role == SiteRole.Administrator && !other.User.IsDisabled), ct);
                if (removesLastAdmin) throw new CmsException("不可停用任一站點的最後一位啟用中管理員。");
            }
            user.IsDisabled = disabled;
            // 單次寫入同時變更狀態與安全戳記，Cookie 與所有 PAT 立即失效。
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    public async Task<IReadOnlyList<MemberSummary>> ListMembersAsync(CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var members = await db.SiteMemberships.AsNoTracking().OrderBy(x => x.User.NormalizedUserName).ThenBy(x => x.UserId)
            .Take(MaximumMembers + 1)
            .Select(x => new MemberSummary(x.UserId, x.User.UserName ?? "", x.Role, x.Version, x.User.IsDisabled, x.DisplayName)).ToListAsync(ct);
        if (members.Count > MaximumMembers) throw new CmsException("站點成員超過 200 筆，無法完整列出。");
        return members;
    }

    public async Task SetMemberAsync(MemberEditInput input, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.Role)) throw new CmsException("站點角色無效。");
        var name = ValidateName(input.UserName, MaximumUserName, "使用者名稱");
        var displayName = (input.DisplayName ?? "").Trim();
        if (displayName.Length > Limits.AuthorName || displayName.Any(char.IsControl))
            throw new CmsException($"顯示名稱最多 {Limits.AuthorName} 字，且不得含控制字元。");
        var normalized = userManager.NormalizeName(name);
        await WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockSiteAsync(db, db.CurrentSiteId, ct);
            // 等候列鎖後重驗，避免已遭降權者利用先前通過的驗證變更成員。
            await admin.RequireRoleAsync(SiteRole.Administrator, ct);
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUserName == normalized, ct)
                ?? throw new CmsException("使用者不存在，請先由全站管理者建立帳號。");
            // 停用帳號無法登入也不該佔用成員名額或管理員計數；先恢復啟用再調整成員。
            if (user.IsDisabled) throw new CmsException("使用者已停用，請先恢復啟用再設定站點成員。");
            var member = await db.SiteMemberships.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            if (input.Version is null)
            {
                if (member is not null) throw VersionConflict();
                if (await db.SiteMemberships.CountAsync(ct) >= MaximumMembers)
                    throw new CmsException("每個站點最多只能有 200 位成員。");
                db.SiteMemberships.Add(new SiteMembership { SiteId = db.CurrentSiteId, UserId = user.Id, Role = input.Role, DisplayName = displayName });
            }
            else
            {
                if (member is null || member.Version != input.Version.Value) throw VersionConflict();
                if (member.Role == SiteRole.Administrator && input.Role != SiteRole.Administrator && !user.IsDisabled)
                    await EnsureAnotherAdministratorAsync(db, user.Id, ct);
                var previousDisplayName = member.DisplayName;
                member.Role = input.Role;
                member.DisplayName = displayName;
                member.Version = Guid.NewGuid();
                // 顯示名稱是內容署名的來源；改名須同步該成員在本站全部內容的署名快照與彙整代稱。
                // ExecuteUpdate 保留站點查詢過濾，不會波及其他站點的內容。
                if (previousDisplayName != displayName)
                {
                    var displaySlug = displayName.Length == 0 ? "" : ContentRules.NormalizeSlug(displayName);
                    await db.Contents.Where(x => x.AuthorId == user.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthorName, displayName)
                            .SetProperty(x => x.AuthorSlug, displaySlug), ct);
                }
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    public async Task RemoveMemberAsync(string userId, Guid version, CancellationToken ct = default)
    {
        await admin.RequireRoleAsync(SiteRole.Administrator, ct);
        ValidateUserId(userId);
        await WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockSiteAsync(db, db.CurrentSiteId, ct);
            await admin.RequireRoleAsync(SiteRole.Administrator, ct);
            var member = await db.SiteMemberships.Include(x => x.User).SingleOrDefaultAsync(x => x.UserId == userId, ct);
            if (member is null || member.Version != version) throw VersionConflict();
            if (member.Role == SiteRole.Administrator && !member.User.IsDisabled)
                await EnsureAnotherAdministratorAsync(db, userId, ct);
            db.SiteMemberships.Remove(member);
            // 憑證每次使用都重驗成員資格；移除成員即停止其本站存取。
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    private static async Task EnsureAnotherAdministratorAsync(CmsDbContext db, string userId, CancellationToken ct)
    {
        if (!await db.SiteMemberships.AsNoTracking().AnyAsync(x => x.UserId != userId &&
            x.Role == SiteRole.Administrator && !x.User.IsDisabled, ct))
            throw new CmsException("不可移除或降低最後一位啟用中站點管理員的權限。");
    }

    internal static async Task<CmsSite> LockSiteAsync(CmsDbContext db, Guid siteId, CancellationToken ct) =>
        await db.Sites.FromSqlInterpolated($"SELECT * FROM \"Sites\" WHERE \"Id\" = {siteId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new CmsException("站點不存在。");

    /// <summary>讀取目前登入者的自助個人資料與當前站點顯示名稱。</summary>
    public async Task<AccountProfileEdit> GetMyProfileAsync(CancellationToken ct = default)
    {
        var user = await admin.RequireNonCredentialUserAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var displayName = await db.SiteMemberships.AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.DisplayName)
            .SingleOrDefaultAsync(ct) ?? "";
        return new AccountProfileEdit
        {
            UserName = user.UserName ?? "",
            Email = user.Email ?? "",
            DisplayName = displayName,
            Bio = user.Bio,
            WebsiteUrl = user.WebsiteUrl,
            AvatarUrl = user.AvatarUrl
        };
    }

    /// <summary>更新目前登入者的個人資料與當前站點顯示名稱（同步更新本站內容署名快照）。</summary>
    public async Task<AccountProfileEdit> UpdateMyProfileAsync(AccountProfileEdit input, CancellationToken ct = default)
    {
        var sessionUser = await admin.RequireNonCredentialUserAsync(ct);
        ArgumentNullException.ThrowIfNull(input);
        var displayName = (input.DisplayName ?? "").Trim();
        if (displayName.Length > Limits.AuthorName || displayName.Any(char.IsControl))
            throw new CmsException($"顯示名稱最多 {Limits.AuthorName} 字，且不得含控制字元。");
        var bio = (input.Bio ?? "").Trim();
        if (bio.Length > Limits.UserBio)
            throw new CmsException($"個人簡介最多 {Limits.UserBio} 字。");
        var website = (input.WebsiteUrl ?? "").Trim();
        if (website.Length > Limits.UserUrl)
            throw new CmsException($"網站網址最多 {Limits.UserUrl} 字。");
        if (website.Length > 0 && (!Uri.TryCreate(website, UriKind.Absolute, out var webUri) || webUri.Scheme is not ("http" or "https")))
            throw new CmsException("網站網址必須是 http 或 https 的絕對網址。");
        var avatar = (input.AvatarUrl ?? "").Trim();
        if (avatar.Length > Limits.UserUrl)
            throw new CmsException($"頭像網址最多 {Limits.UserUrl} 字。");
        if (avatar.Length > 0)
        {
            var isLocalMedia = avatar.StartsWith("/media/", StringComparison.Ordinal) &&
                ContentRules.IsSafeMediaFileName(avatar["/media/".Length..]);
            var isHttpUrl = Uri.TryCreate(avatar, UriKind.Absolute, out var avatarUri) && avatarUri.Scheme is "http" or "https";
            if (!isLocalMedia && !isHttpUrl)
                throw new CmsException("頭像網址必須是 http／https 絕對網址或站內 /media/ 圖片路徑。");
        }
        var email = (input.Email ?? "").Trim();
        if (email.Length > 0 && (email.Length > 256 || !email.Contains('@') || email.Any(char.IsControl)))
            throw new CmsException("電子郵件格式不正確。");

        return await WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == sessionUser.Id, ct)
                ?? throw new CmsException("帳號不存在。");
            user.Bio = bio;
            user.WebsiteUrl = website;
            user.AvatarUrl = avatar;
            user.Email = email;
            user.NormalizedEmail = email.Length == 0 ? null : userManager.NormalizeEmail(email);

            var member = await db.SiteMemberships.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            if (member is not null && member.DisplayName != displayName)
            {
                member.DisplayName = displayName;
                member.Version = Guid.NewGuid();
            }
            var displaySlug = displayName.Length == 0 ? "" : ContentRules.NormalizeSlug(displayName);
            await db.Contents.Where(x => x.AuthorId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthorName, displayName)
                    .SetProperty(x => x.AuthorSlug, displaySlug), ct);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new AccountProfileEdit
            {
                UserName = user.UserName ?? "",
                Email = user.Email ?? "",
                DisplayName = displayName,
                Bio = user.Bio,
                WebsiteUrl = user.WebsiteUrl,
                AvatarUrl = user.AvatarUrl
            };
        });
    }

    /// <summary>自助修改目前登入者的密碼。</summary>
    public async Task ChangeMyPasswordAsync(ChangePasswordInput input, CancellationToken ct = default)
    {
        var sessionUser = await admin.RequireNonCredentialUserAsync(ct);
        ArgumentNullException.ThrowIfNull(input);
        var newPassword = input.NewPassword;
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length is < 8 or > 1024 ||
            newPassword.Distinct().Count() < 4 || !newPassword.Any(char.IsAsciiDigit) ||
            !newPassword.Any(char.IsAsciiLetterLower) || !newPassword.Any(char.IsAsciiLetterUpper) ||
            !newPassword.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new CmsException("新密碼須為 8–1024 字，包含大小寫英文字母、數字、符號與至少 4 種不同字元。");

        var user = await userManager.FindByIdAsync(sessionUser.Id)
            ?? throw new CmsException("帳號不存在。");
        var result = await userManager.ChangePasswordAsync(user, input.CurrentPassword ?? "", newPassword);
        if (!result.Succeeded)
            throw new CmsException("目前密碼不正確，或新密碼不符合規則。");
    }

    /// <summary>全站管理者重設一般使用者的兩步驗證（TOTP 與恢復碼），並輪替安全戳記使其重新登入。</summary>
    public async Task ResetUserTwoFactorAsync(string id, CancellationToken ct = default)
    {
        var actor = await admin.RequireNetworkAdminAsync(ct);
        ValidateUserId(id);
        await WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw new CmsException("使用者不存在。");
            if (user.SingletonKey == 1)
                throw new CmsException("全站管理者的兩步驟驗證請使用伺服器端 admin-reset 命令重設。");
            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = "";
            user.RecoveryCodeHashes = "";
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            db.AuditLogs.Add(new AuditLog
            {
                SiteId = db.CurrentSiteId,
                UserId = actor.Id,
                UserName = actor.UserName ?? "",
                Action = "user.2fa_reset",
                Target = user.UserName ?? user.Id,
                CreatedAt = timeProvider.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    /// <summary>匯出目前登入者的完整個人資料（GDPR／個資匯出），不含密碼雜湊或 TOTP 密鑰。</summary>
    public async Task<PersonalDataExport> ExportMyPersonalDataAsync(CancellationToken ct = default)
    {
        var user = await admin.RequireNonCredentialUserAsync(ct);
        return await BuildPersonalDataExportAsync(user.Id, ct);
    }

    /// <summary>抹除目前登入者的個人資料並將其已發表內容匿名化。</summary>
    public async Task<PersonalDataEraseResult> EraseMyPersonalDataAsync(CancellationToken ct = default)
    {
        var user = await admin.RequireNonCredentialUserAsync(ct);
        return await EraseUserPersonalDataAsync(user.Id, ct);
    }

    /// <summary>全站管理者匯出指定帳號的個人資料。</summary>
    public async Task<PersonalDataExport> ExportUserPersonalDataAsync(string userId, CancellationToken ct = default)
    {
        await admin.RequireNetworkAdminAsync(ct);
        ValidateUserId(userId);
        return await BuildPersonalDataExportAsync(userId, ct);
    }

    private async Task<PersonalDataExport> BuildPersonalDataExportAsync(string userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new CmsException("使用者不存在。");
        var memberships = await db.SiteMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.Site.Name)
            .Select(x => new PersonalSiteMembershipItem(
                x.SiteId, x.Site.Name, x.Site.Slug, x.Role.ToString(), x.DisplayName))
            .ToListAsync(ct);
        var contents = await db.Contents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.AuthorId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .Select(x => new PersonalContentItem(
                x.SiteId, x.Id, x.Kind.ToString(), x.Title, x.Slug, x.Status.ToString(), x.CreatedAt, x.PublishedAt))
            .ToListAsync(ct);
        var audits = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .Select(x => new PersonalAuditItem(x.SiteId, x.Action, x.Target, x.CreatedAt))
            .ToListAsync(ct);
        return new PersonalDataExport(
            user.Id,
            user.UserName ?? "",
            user.Email ?? "",
            user.Bio,
            user.WebsiteUrl,
            user.AvatarUrl,
            user.TwoFactorEnabled,
            timeProvider.GetUtcNow(),
            memberships,
            contents,
            audits);
    }

    /// <summary>
    /// 個人資料抹除與匿名化工作流程：清空帳號個資（信箱、簡介、網站、頭像、2FA）、撤銷所有 API 憑證、
    /// 將該帳號在各站撰寫的內容改為匿名（AuthorId=null, AuthorName="", AuthorSlug=""）、移除成員資格並停用帳號。
    /// 若為任一站點最後一位啟用中管理員或全站管理者，將拒絕操作以保護站點可管理性。
    /// </summary>
    public async Task<PersonalDataEraseResult> EraseUserPersonalDataAsync(string userId, CancellationToken ct = default)
    {
        var actor = await admin.RequireNonCredentialUserAsync(ct);
        ValidateUserId(userId);
        // 允許本人自助申請抹除，或由全站管理者對指定帳號執行。
        if (actor.Id != userId)
            await admin.RequireNetworkAdminAsync(ct);

        return await WriteSafelyAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var sites = await db.Sites.FromSqlRaw("SELECT * FROM \"Sites\" ORDER BY \"Id\" LIMIT 201 FOR UPDATE")
                .AsNoTracking().ToListAsync(ct);
            if (sites.Count > MaximumSites) throw new CmsException("站點超過 200 筆，請先處理站點上限。");
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw new CmsException("使用者不存在。");
            if (user.SingletonKey == 1)
                throw new CmsException("不可抹除全站管理者的個人資料。");
            if (!user.IsDisabled)
            {
                var removesLastAdmin = await db.SiteMemberships.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(m => m.UserId == userId && m.Role == SiteRole.Administrator &&
                        !db.SiteMemberships.IgnoreQueryFilters().Any(other => other.SiteId == m.SiteId &&
                            other.UserId != userId && other.Role == SiteRole.Administrator && !other.User.IsDisabled), ct);
                if (removesLastAdmin)
                    throw new CmsException("不可抹除任一站點的最後一位啟用中管理員；請先指派其他站點管理員。");
            }

            var displayNames = await db.SiteMemberships.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.UserId == userId && x.DisplayName != "")
                .Select(x => x.DisplayName)
                .Distinct()
                .ToListAsync(ct);
            if (!string.IsNullOrWhiteSpace(user.UserName))
                displayNames.Add(user.UserName);

            var anonymizedContents = await db.Contents.IgnoreQueryFilters()
                .Where(x => x.AuthorId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AuthorId, (string?)null)
                    .SetProperty(x => x.AuthorName, "")
                    .SetProperty(x => x.AuthorSlug, ""), ct);

            var anonymizedComments = 0;
            if (displayNames.Count > 0)
            {
                anonymizedComments = await db.Comments.IgnoreQueryFilters()
                    .Where(x => displayNames.Contains(x.AuthorName))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.AuthorName, "匿名讀者")
                        .SetProperty(x => x.Url, ""), ct);
            }

            var revokedCredentials = await db.ApiCredentials.IgnoreQueryFilters()
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync(ct);

            var removedMemberships = await db.SiteMemberships.IgnoreQueryFilters()
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync(ct);

            var anonToken = "erased-" + user.Id[..Math.Min(12, user.Id.Length)];
            user.UserName = anonToken;
            user.NormalizedUserName = anonToken.ToUpperInvariant();
            user.Email = "";
            user.NormalizedEmail = null;
            user.Bio = "";
            user.WebsiteUrl = "";
            user.AvatarUrl = "";
            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = "";
            user.RecoveryCodeHashes = "";
            user.IsDisabled = true;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

            db.AuditLogs.Add(new AuditLog
            {
                SiteId = db.CurrentSiteId,
                UserId = actor.Id,
                UserName = actor.UserName ?? "",
                Action = "user.personal_data_erased",
                Target = user.Id,
                CreatedAt = timeProvider.GetUtcNow()
            });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new PersonalDataEraseResult(user.Id, anonymizedContents, anonymizedComments, removedMemberships, revokedCredentials);
        });
    }

    private static SiteSummary Summary(CmsSite site) =>
        new(site.Id, site.Name, site.Slug, site.HostName, site.IsActive, site.Version);

    private static bool IsDnsLabel(string value) => value.Length is >= 1 and <= 63 &&
        char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static string ValidateName(string? value, int maximum, string label)
    {
        var result = value?.Trim() ?? "";
        if (result.Length == 0 || result.Length > maximum || result.Any(char.IsControl))
            throw new CmsException($"{label}須為 1–{maximum} 字，且不得含控制字元。");
        return result;
    }

    private static void ValidateUserId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 450 || value.Any(char.IsControl))
            throw new CmsException("使用者識別無效。");
    }

    private static CmsException VersionConflict() => new("資料已被更新或刪除，請重新載入後再操作。");

    // 交易提交也納入錯誤轉譯；不把 PostgreSQL 訊息、SQL 或 Identity 內部錯誤傳給呼叫端。
    internal static async Task<T> WriteSafelyAsync<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (DbUpdateConcurrencyException) { throw VersionConflict(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new CmsException("名稱、主機或資料已存在，請重新載入並使用不同的值。"); }
        catch (DbUpdateException) { throw new CmsException("無法儲存資料，請確認輸入並重新載入後再試。"); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        { throw VersionConflict(); }
        catch (DbException) { throw new CmsException("資料庫暫時無法完成操作，請稍後再試。"); }
    }
}
