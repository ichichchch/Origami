namespace Origami.Core.Models;

public enum WidgetArea { Sidebar = 0, Footer = 1 }
public enum WidgetType { RecentPosts = 0, Categories = 1, Tags = 2, Search = 3, Text = 4, RecentComments = 5, RssFeed = 6 }

/// <summary>
/// 受控小工具：以宣告方式組成側欄與頁尾的內容區塊，只有內建型別、不載入任何程式碼。
/// 文字小工具的 Markdown 與內容走同一條安全渲染管線。
/// </summary>
public sealed class SiteWidget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; } = SiteDefaults.DefaultSiteId;
    public WidgetArea Area { get; set; }
    public WidgetType Type { get; set; }
    /// <summary>顯示標題；空字串時前台以型別預設標題呈現。</summary>
    public string Title { get; set; } = "";
    /// <summary>序列化後的型別組態（JSON）；欄位與格式由 WidgetService 依型別驗證。</summary>
    public string ConfigJson { get; set; } = "{}";
    /// <summary>同區域內的顯示順序；數字越小越靠前。排序查詢一律以 Area 分組後依此排序。</summary>
    public int Position { get; set; }
    public bool IsVisible { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public CmsSite Site { get; set; } = null!;
}

public static class WidgetTypes
{
    public static readonly IReadOnlyList<WidgetType> All =
        [WidgetType.RecentPosts, WidgetType.Categories, WidgetType.Tags, WidgetType.Search, WidgetType.Text,
            WidgetType.RecentComments, WidgetType.RssFeed];

    public static bool IsValid(WidgetType type) => All.Contains(type);

    /// <summary>組態 JSON 的固定形狀；未使用的欄位由服務層依型別清空。</summary>
    public sealed record WidgetConfig(int? Count = null, string? Markdown = null, string? Url = null);

    public static WidgetConfig ParseConfig(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<WidgetConfig>(json, JsonOptions) ?? new WidgetConfig();
        }
        catch (JsonException)
        {
            return new WidgetConfig();
        }
    }

    public static string SerializeConfig(WidgetConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
