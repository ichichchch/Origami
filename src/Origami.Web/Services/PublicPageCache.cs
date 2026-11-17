using Microsoft.AspNetCore.OutputCaching;

namespace Origami.Web.Services;

/// <summary>
/// 匿名頁面輸出快取的准入規則：僅無任何 Cookie 的 GET/HEAD、且限無表單的公開路徑。
/// 含防偽表單的頁面（/posts/{slug} 與 /pages/ 詳情、/search、/preview、/account、/admin）
/// 絕不整頁快取——快取 HTML 中的防偽權杖跨工作階段失效，會使評論 POST 被拒。
/// 帶任何 Cookie（登入、文化偏好、受保護內容解鎖）或已驗證的請求一律直渲染。
/// </summary>
public static class PublicPageCachePolicy
{
    public static bool Cacheable(OutputCacheContext context)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated == true) return false;
        if (http.Request.Cookies.Count > 0) return false;
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)) return false;
        var path = http.Request.Path.Value ?? "/";
        // 僅快取無表單的 HTML 列表頁；機器端點（feed／sitemap／robots）必須即時反映發布狀態，不進快取。
        return path == "/" || path == "/posts" || path == "/archives"
            || path.StartsWith("/archives", StringComparison.Ordinal) || path.StartsWith("/author", StringComparison.Ordinal);
    }
}

/// <summary>
/// 詳情頁條件請求（ETag / 304）：輸出快取刻意排除的內容詳情改以身體雜湊 ETag 支援
/// If-None-Match 再驗證，降低匿名重複訪問與爬蟲抓取的傳輸成本。有 Cookie 或已驗證
/// 的請求直接透傳，不參與比對（其回應本就可能個人化）。
/// </summary>
public sealed class DetailPageEtagMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var isDetail = (path.StartsWith("/posts", StringComparison.Ordinal) && path != "/posts")
            || path.StartsWith("/pages", StringComparison.Ordinal);
        if (!isDetail || (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            || context.User.Identity?.IsAuthenticated == true)
        {
            await next(context);
            return;
        }
        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try { await next(context); }
        finally { context.Response.Body = original; }
        var bytes = buffer.ToArray();
        if (context.Response.StatusCode != StatusCodes.Status200OK || bytes.Length == 0)
        {
            await original.WriteAsync(bytes, context.RequestAborted);
            return;
        }
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(HashablePortion(bytes)))[..16] + "\"";
        if (context.Request.Headers.IfNoneMatch.ToString() == etag)
        {
            // 回應已無主體：清空 Content-Length 以符合 304 語義。
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.ContentLength = 0;
            return;
        }
        context.Response.Headers.ETag = etag;
        await original.WriteAsync(bytes, context.RequestAborted);
    }

    /// <summary>
    /// Blazor SSR 在每個含互動元件的頁面尾部附加逐請求加密的
    /// Blazor-Server-Component-State 註解；ETag 只代表可見內容是否變化，
    /// 雜湊範圍排除該註解（304 時客戶端保留自己已有的副本，含其專屬狀態註解）。
    /// </summary>
    private static byte[] HashablePortion(byte[] bytes)
    {
        var marker = "<!--Blazor-Server-Component-State:"u8;
        var index = bytes.AsSpan().IndexOf(marker); // 大小寫敏感的位元組搜尋。
        return index < 0 ? bytes : bytes[..index];
    }
}
