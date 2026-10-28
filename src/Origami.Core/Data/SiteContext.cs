namespace Origami.Core.Data;

/// <summary>由主機以 scoped 生命週期註冊，並在建立 DbContext 前解析當前站點。</summary>
public sealed class SiteContext
{
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
}
