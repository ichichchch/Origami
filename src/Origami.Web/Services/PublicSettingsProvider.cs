namespace Origami.Web.Services;

/// <summary>每請求快取的公開站點設定：可見度中介層與版面共用同一次 Api 讀取，避免重複往返。</summary>
public sealed class PublicSettingsProvider(OrigamiClient api)
{
    private PublicSiteSettings? _cached;

    public async Task<PublicSiteSettings> GetAsync(CancellationToken ct = default) =>
        _cached ??= await api.GetPublicSettingsAsync(ct) ?? PublicSiteSettings.Default;
}
