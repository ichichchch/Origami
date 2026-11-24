namespace Origami.Web.Services;

/// <summary>
/// 站點預設介面語言：請求文化提供者在 URL、Cookie 與登入使用者偏好皆未指定時採用。
/// 與版面殼層共用每請求快取，設定儲存後的下一個請求立即採用新值。
/// </summary>
public sealed class SiteDefaultLanguageReader(PublicSettingsProvider settings)
{
    /// <summary>取得站點預設語言鍵；Api 不可用或未設定時回 null（沿用產品預設）。</summary>
    public async Task<string?> GetAsync(HttpContext context, CancellationToken ct)
    {
        try
        {
            var language = (await settings.GetAsync(ct)).DefaultUiLanguage;
            return SupportedUiLanguages.Normalize(language);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
