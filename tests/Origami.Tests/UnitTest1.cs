namespace Origami.Tests;

public sealed class ContentRulesTests
{
    [Theory]
    [InlineData("  Hello, WORLD!  ", "hello-world")]
    [InlineData("one___two / three", "one-two-three")]
    [InlineData("ＡＢＣ １２３", "abc-123")]
    [InlineData("中文 分類", "中文-分類")]
    [InlineData("Cafe\u0301", "café")]
    [InlineData("---Already-Normal---", "already-normal")]
    [InlineData("123", "123")]
    public void Slugs_are_normalized_and_idempotent(string input, string expected)
    {
        Assert.Equal(expected, ContentRules.NormalizeSlug(input));
        Assert.Equal(expected, ContentRules.NormalizeSlug(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("___ / ! ---")]
    public void Empty_or_separator_only_slugs_are_rejected(string? input) =>
        Assert.Throws<CmsException>(() => ContentRules.NormalizeSlug(input!));

    [Fact]
    public void Slug_length_is_checked_after_normalization()
    {
        var maximum = new string('a', Limits.Slug);
        Assert.Equal(maximum, ContentRules.NormalizeSlug(maximum));
        Assert.Equal(maximum, ContentRules.NormalizeSlug("  " + maximum + "!!"));
        Assert.Throws<CmsException>(() => ContentRules.NormalizeSlug(maximum + "a"));
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 50, 100)]
    [InlineData(int.MaxValue, 1, int.MaxValue - 1)]
    [InlineData(42_949_672, 50, 2_147_483_550)]
    public void Pagination_offsets_are_exact(int page, int size, int expected) =>
        Assert.Equal(expected, ContentRules.Offset(page, size));

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 51)]
    [InlineData(int.MaxValue, 2)]
    [InlineData(42_949_673, 50)]
    public void Invalid_or_overflowing_pagination_is_rejected(int page, int size) =>
        Assert.Throws<CmsException>(() => ContentRules.Offset(page, size));

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(20, 10, 2)]
    public void Total_pages_round_up_and_empty_results_have_one_page(int total, int size, int expected) =>
        Assert.Equal(expected, new PagedResult<int>([], total, 1, size).TotalPages);

    [Theory]
    [InlineData("content-title", 0, false)]
    [InlineData("content-title", 1, true)]
    [InlineData("content-title", Limits.Title, true)]
    [InlineData("content-title", Limits.Title + 1, false)]
    [InlineData("content-slug", 0, true)]
    [InlineData("content-slug", Limits.Slug, true)]
    [InlineData("content-slug", Limits.Slug + 1, false)]
    [InlineData("content-body", 0, true)]
    [InlineData("content-body", Limits.Body, true)]
    [InlineData("content-body", Limits.Body + 1, false)]
    [InlineData("taxonomy-name", 0, false)]
    [InlineData("taxonomy-name", Limits.TaxonomyName, true)]
    [InlineData("taxonomy-name", Limits.TaxonomyName + 1, false)]
    [InlineData("taxonomy-slug", 0, true)]
    [InlineData("taxonomy-slug", Limits.Slug, true)]
    [InlineData("taxonomy-slug", Limits.Slug + 1, false)]
    [InlineData("site-name", 0, false)]
    [InlineData("site-name", Limits.SiteName, true)]
    [InlineData("site-name", Limits.SiteName + 1, false)]
    [InlineData("site-description", 0, true)]
    [InlineData("site-description", Limits.Description, true)]
    [InlineData("site-description", Limits.Description + 1, false)]
    public void Input_annotations_enforce_exact_length_boundaries(string field, int length, bool valid)
    {
        var value = new string('x', length);
        object input = field switch
        {
            "content-title" => new ContentEdit { Title = value },
            "content-slug" => new ContentEdit { Title = "Title", Slug = value },
            "content-body" => new ContentEdit { Title = "Title", Markdown = value },
            "taxonomy-name" => new TaxonomyEdit { Name = value },
            "taxonomy-slug" => new TaxonomyEdit { Name = "Name", Slug = value },
            "site-name" => new SiteSettings { Name = value },
            "site-description" => new SiteSettings { Name = "Name", Description = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        if (valid) ContentRules.Validate(input);
        else Assert.Throws<CmsException>(() => ContentRules.Validate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void Required_names_reject_null_and_whitespace(string? value)
    {
        Assert.Throws<CmsException>(() => ContentRules.Validate(new ContentEdit { Title = value! }));
        Assert.Throws<CmsException>(() => ContentRules.Validate(new TaxonomyEdit { Name = value! }));
        Assert.Throws<CmsException>(() => ContentRules.Validate(new SiteSettings { Name = value! }));
    }
}
