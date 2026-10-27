using Origami.Mobile.Models;

namespace Origami.Mobile.ViewModels;

/// <summary>文章列表或搜尋結果中的單一列。</summary>
public sealed record ItemModel(string Title, string Subtitle, string Slug, string Badge);
