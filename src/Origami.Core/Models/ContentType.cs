namespace Origami.Core.Models;

/// <summary>
/// 自訂內容類型：以宣告方式定義的內容種類，不載入任何程式碼。
/// 類型內容仍以 <see cref="Content"/> 儲存，並以 <see cref="Content.ContentTypeId"/> 指向本類型。
/// </summary>
public sealed class ContentType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public string Name { get; set; } = "";
    /// <summary>公開區塊的路由代稱；同一站點內唯一，且不得與保留路徑衝突。</summary>
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>可指派內建分類。</summary>
    public bool SupportsCategories { get; set; }
    /// <summary>可指派內建標籤。</summary>
    public bool SupportsTags { get; set; }
    /// <summary>同類內容可指定上層內容，形成階層。</summary>
    public bool SupportsHierarchy { get; set; }
    /// <summary>封存後不再接受新內容，既有內容保留但公開路由關閉。</summary>
    public bool IsArchived { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<CustomTaxonomy> Taxonomies { get; set; } = new List<CustomTaxonomy>();
}

/// <summary>自訂分類法：附屬於單一內容類型的詞彙集。</summary>
public sealed class CustomTaxonomy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid ContentTypeId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>詞彙可有上層詞彙。</summary>
    public bool IsHierarchical { get; set; }
    public bool IsArchived { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public ContentType ContentType { get; set; } = null!;
    public ICollection<CustomTaxonomyTerm> Terms { get; set; } = new List<CustomTaxonomyTerm>();
}

/// <summary>自訂分類法底下的詞彙。</summary>
public sealed class CustomTaxonomyTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid TaxonomyId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid? ParentId { get; set; }
    public CustomTaxonomyTerm? Parent { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public CustomTaxonomy Taxonomy { get; set; } = null!;
}

/// <summary>內容與自訂詞彙的關聯；同一內容與詞彙只建立一列。</summary>
public sealed class ContentCustomTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public Guid ContentId { get; set; }
    public Guid TermId { get; set; }
    public Content Content { get; set; } = null!;
    public CustomTaxonomyTerm Term { get; set; } = null!;
}
