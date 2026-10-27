using System.Text.Json.Serialization;

namespace Origami.Mobile.Models;

/// <summary>Origami /api/v1 端點的唯讀 JSON 合約（camelCase，列舉以字串表示）。</summary>
public sealed record ApiPagedResult<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize);

public sealed record ApiContentSummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("excerpt")] string Excerpt,
    [property: JsonPropertyName("isSticky")] bool IsSticky,
    [property: JsonPropertyName("isProtected")] bool IsProtected);

public sealed record ApiNavigationItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug);

public sealed record ApiContentDetail(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("markdown")] string Markdown,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("excerpt")] string Excerpt,
    [property: JsonPropertyName("categories")] IReadOnlyList<ApiNavigationItem> Categories,
    [property: JsonPropertyName("tags")] IReadOnlyList<ApiNavigationItem> Tags);

public sealed record ApiSearchHit(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("snippet")] string Snippet,
    [property: JsonPropertyName("source")] string Source);

public sealed record ApiSearchResult(
    [property: JsonPropertyName("hits")] IReadOnlyList<ApiSearchHit> Hits,
    [property: JsonPropertyName("semanticAvailable")] bool SemanticAvailable);
