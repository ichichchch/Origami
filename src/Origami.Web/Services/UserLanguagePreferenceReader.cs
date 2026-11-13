using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Origami.Core.Data;
using Origami.Core.Models;

namespace Origami.Web.Services;

/// <summary>
/// 供要求文化中介層讀取使用者介面語言偏好。與登入流程同屬主機層必要讀取（Web 主機持有
/// DbContextFactory 先例）；以短快取避免每請求查詢，同一應用實例內變更後立即失效。
/// </summary>
public sealed class UserLanguagePreferenceReader(IDbContextFactory<CmsDbContext> factory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>取得使用者偏好語言鍵；未設定或找不到使用者時回 null（快取空字串哨兵）。</summary>
    public async Task<string?> GetAsync(string userId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey(userId), out string? cached) && cached is not null)
            return cached.Length == 0 ? null : cached;
        await using var db = await factory.CreateDbContextAsync(ct);
        var language = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => x.UiLanguage).SingleOrDefaultAsync(ct) ?? "";
        var normalized = SupportedUiLanguages.Normalize(language) ?? "";
        cache.Set(CacheKey(userId), normalized, CacheDuration);
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>偏好變更寫入成功後由本程序呼叫，讓下一個請求立即生效。</summary>
    public void Evict(string userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(string userId) => "ui-language:" + userId;
}
