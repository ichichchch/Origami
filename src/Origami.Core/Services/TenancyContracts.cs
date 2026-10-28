namespace Origami.Core.Services;

/// <summary>跨 BFF、API 與授權服務共用的信任邊界名稱。</summary>
public static class AccessClaims
{
    public const string SiteHeader = "X-Origami-Site";
    public const string UserHeader = "X-Origami-User";
    public const string StampHeader = "X-Admin-Security-Stamp";
    public const string Bff = "origami:bff";
    public const string CredentialId = "origami:credential";
    public const string CredentialScope = "origami:scope";
    public const string SiteId = "origami:site";
}

/// <summary>已辨識身分但沒有操作權限；HTTP 層轉成 403，而非登出。</summary>
public sealed class AccessDeniedException(string message = "你沒有此站點的操作權限。") : Exception(message);

public sealed record SiteSummary(Guid Id, string Name, string Slug, string? HostName, bool IsActive, Guid Version);
public sealed record SiteAccessInfo(string UserId, string UserName, Guid SiteId, bool IsNetworkAdmin, SiteRole? Role)
{
    public bool CanRead => IsNetworkAdmin || Role is not null;
    public bool CanEdit => IsNetworkAdmin || Role is SiteRole.Editor or SiteRole.Administrator;
    public bool CanAdminister => IsNetworkAdmin || Role == SiteRole.Administrator;
}

public sealed class SiteCreateInput
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string HostName { get; set; } = "";
}

public sealed class SiteUpdateInput
{
    public string Name { get; set; } = "";
    public string? HostName { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; }
}

public sealed record UserSummary(string Id, string UserName, bool IsDisabled, bool IsNetworkAdmin);
public sealed class UserCreateInput
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}
public sealed record UserStateInput(bool IsDisabled);
public sealed record MemberSummary(string UserId, string UserName, SiteRole Role, Guid Version, bool IsDisabled);
public sealed class MemberEditInput
{
    public string UserName { get; set; } = "";
    public SiteRole Role { get; set; } = SiteRole.Reader;
    public Guid? Version { get; set; }
}

public sealed record CredentialSummary(Guid Id, string Name, ApiCredentialScope Scope,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
public sealed class CredentialCreateInput
{
    public string Name { get; set; } = "";
    public ApiCredentialScope Scope { get; set; } = ApiCredentialScope.Read;
    public DateTimeOffset ExpiresAt { get; set; }
}
/// <summary>僅建立成功時回傳一次；不可寫入日誌、持久化 UI 狀態或後續列表。</summary>
public sealed record CreatedCredential(Guid Id, string Token, DateTimeOffset ExpiresAt);
