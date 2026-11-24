namespace Origami.Core.Models;

/// <summary>內容範本：建立新內容時套用的 Markdown 骨架；只影響新內容，不更動既有內容。</summary>
public sealed class ContentTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string Name { get; set; } = "";
    /// <summary>套用對象：文章或獨立頁面；自訂內容類型的範本另以 ContentTypeId 指定（Kind 為文章）。</summary>
    public ContentKind Kind { get; set; } = ContentKind.Post;
    public Guid? ContentTypeId { get; set; }
    /// <summary>建議標題；空字串表示不預填標題。</summary>
    public string Title { get; set; } = "";
    public string Markdown { get; set; } = "";
    /// <summary>顯示順序；數字越小越前面。</summary>
    public int Position { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

/// <summary>可重複使用的文字片段：在編輯器插入到游標位置。</summary>
public sealed class ContentSnippet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string Name { get; set; } = "";
    public string Markdown { get; set; } = "";
    /// <summary>顯示順序；數字越小越前面。</summary>
    public int Position { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
