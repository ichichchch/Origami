namespace Origami.Web.Services;

/// <summary>每請求快取的公開版面資料：文化解析、可見度標頭與版面殼層共用同一次 Api 讀取。</summary>
public sealed class PublicSettingsProvider(OrigamiClient api)
{
    private PublicLayout? _cached;

    public async Task<PublicLayout> GetLayoutAsync(CancellationToken ct = default) =>
        _cached ??= await api.GetPublicLayoutAsync(ct) ?? new PublicLayout(PublicSiteSettings.Default, [], []);

    public async Task<PublicSiteSettings> GetAsync(CancellationToken ct = default) =>
        (await GetLayoutAsync(ct)).Settings;
}
