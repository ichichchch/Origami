using Origami.Mobile.Models;

namespace Origami.Mobile.ViewModels;

/// <summary>A single row in the posts list or search results.</summary>
public sealed record ItemModel(string Title, string Subtitle, string Slug, string Badge);
