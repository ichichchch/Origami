namespace Origami.Web.Services;

/// <summary>只向 Api 查詢明確主機映射；不自行查庫或猜測預設站點。</summary>
public sealed class SiteResolver(HttpClient client, IMemoryCache cache, SiteResolveThrottle throttle)
{
    // Items 只由本機中介軟體設定，不接受任何瀏覽器站點標頭。
    public static readonly object ResolvedSiteKey = new();

    // 正負結果皆快取：站點登錄／停用最遲 30 秒生效，換取不必對每個請求（含靜態資源）打 Api。
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<SiteSummary?> ResolveAsync(string host, CancellationToken ct = default) =>
        await ResolveAsync(host, null, ct);

    /// <param name="source">消耗冷解析預算的來源鍵（一般是連線 IP）；null 時與電路共用同一份預算。</param>
    public async Task<SiteSummary?> ResolveAsync(string host, string? source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        var key = "site-host:" + host.Trim().TrimEnd('.').ToLowerInvariant();
        if (cache.TryGetValue(key, out SiteSummary? cached)) return cached;
        // 快取外的上游解析消耗每來源的預算，防止匿名流量以隨機 Host 把網站當放大器打 Api。
        if (!throttle.TryAcquire(source)) throw new SiteResolutionException();
        SiteSummary? site;
        try
        {
            using var response = await client.GetAsync("api/v1/sites/resolve?host=" + Uri.EscapeDataString(host), ct);
            if (response.StatusCode == HttpStatusCode.NotFound) site = null;
            else if (!response.IsSuccessStatusCode) throw new SiteResolutionException();
            else
            {
                site = await response.Content.ReadFromJsonAsync<SiteSummary>(cancellationToken: ct);
                if (site is null || site.Id == Guid.Empty) throw new SiteResolutionException();
                site = site.IsActive ? site : null;
            }
        }
        catch (HttpRequestException) { throw new SiteResolutionException(); }
        catch (JsonException) { throw new SiteResolutionException(); }
        catch (NotSupportedException) { throw new SiteResolutionException(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SiteResolutionException(); }
        // 連線失敗等暫時性錯誗不寫入快取，避免單次故障把整批主機封鎖 30 秒。
        cache.Set(key, site, CacheDuration);
        return site;
    }
}

/// <summary>站點冷解析（快取外的上游查詢）的每來源預算；獨立於登入限流，避免互相排擠。</summary>
public sealed class SiteResolveThrottle(TimeProvider clock)
{
    public const int MaxAttempts = 60;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly LoginThrottle budget = new(clock, MaxAttempts, maxSources: 4096, Window);

    public bool TryAcquire(string? source) => budget.TryAcquire(source);
}

/// <summary>不包含 API 位址、憑證或底層錯誤內容。</summary>
public sealed class SiteResolutionException() : Exception("站點服務暫時無法使用，請稍後重試。");

/// <summary>每個 Interactive Server 電路有獨立 scope，必須先核定站點才建立元件。</summary>
public sealed class SiteCircuitHandler(
    SiteResolver resolver,
    SiteContext siteContext,
    NavigationManager navigation,
    IHttpContextAccessor accessor,
    AdminSession session) : CircuitHandler
{
    private string? boundHost;
    public override int Order => int.MinValue;

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var uri = new Uri(navigation.Uri);
        var site = await resolver.ResolveAsync(uri.Host, cancellationToken)
            ?? throw new AccessDeniedException("此主機未登錄或站點已停用，請重新開啟已登錄的站點。");
        // 若握手 HTTP 有已核定站點，電路提交的 URI 不得指向不同租戶。
        if (accessor.HttpContext?.Items[SiteResolver.ResolvedSiteKey] is SiteSummary handshake && handshake.Id != site.Id)
            throw new AccessDeniedException("電路站點與連線主機不符，請重新載入。");
        siteContext.SiteId = site.Id;
        boundHost = uri.IdnHost;
        session.UseCircuitAuthentication();
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // 重連仍沿用舊電路 scope；不能把另一主機的握手接到這份站點狀態。
        var uri = new Uri(navigation.Uri);
        if (boundHost is null || !string.Equals(uri.IdnHost, boundHost, StringComparison.OrdinalIgnoreCase))
            throw new AccessDeniedException("電路主機已變更，請整頁重新載入。");
        if (accessor.HttpContext?.Items[SiteResolver.ResolvedSiteKey] is SiteSummary handshake && handshake.Id != siteContext.SiteId)
            throw new AccessDeniedException("重連站點與原電路不符，請整頁重新載入。");
        var site = await resolver.ResolveAsync(uri.Host, cancellationToken);
        if (site is null || site.Id != siteContext.SiteId)
            throw new AccessDeniedException("原站點已停用或主機映射已變更，請整頁重新載入。");
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) => async context =>
    {
        // 導覽事件不能重新指定 scope 的站點。跨站必須整頁導覽並重新登入。
        if (boundHost is not null && !string.Equals(new Uri(navigation.Uri).IdnHost, boundHost, StringComparison.OrdinalIgnoreCase))
            throw new AccessDeniedException("切換站點需要整頁開啟該站的登錄主機。");
        await next(context);
    };
}
