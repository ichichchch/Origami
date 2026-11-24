namespace Origami.Tests;

/// <summary>內容範本與可重複片段：站點層級資產，唯讀成員可檢視、編輯者以上可修改。</summary>
[Collection(PostgresCollection.Name)]
public sealed class WritingAssetsTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Templates_and_snippets_support_editing_with_versions()
    {
        using var admin = await App.AdministratorAsync();
        var template = await admin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "每月摘要", Kind = ContentKind.Post, Title = "本月摘要", Markdown = "## 重點\n\n- \n\n## 下月計畫\n\n"
        });
        var pageTemplate = await admin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "著陸頁", Kind = ContentKind.Page, Markdown = "## 立即開始\n\n"
        });
        var snippet = await admin.Writing.SaveSnippetAsync(new WritingAssetsService.SnippetEdit
        {
            Name = "版權聲明", Markdown = "> 未經授權請勿轉載。"
        });
        Assert.Equal(2, (await admin.Writing.ListTemplatesAsync()).Count);
        Assert.Equal("版權聲明", Assert.Single(await admin.Writing.ListSnippetsAsync()).Name);

        // 更新須攜帶版本；過期版本被拒絕。
        var edit = new WritingAssetsService.TemplateEdit
        {
            Id = template.Id, Version = template.Version, Name = "每月摘要（更新）", Kind = ContentKind.Post,
            Markdown = "## 更新後的重點\n\n"
        };
        var updated = await admin.Writing.SaveTemplateAsync(edit);
        Assert.Equal("每月摘要（更新）", updated.Name);
        Assert.NotEqual(template.Version, updated.Version);
        await Assert.ThrowsAnyAsync<Exception>(() => admin.Writing.DeleteTemplateAsync(template.Id, template.Version));

        // 名稱與內容不可空白；範本可指向自訂內容類型。
        await Assert.ThrowsAsync<CmsException>(() => admin.Writing.SaveSnippetAsync(new WritingAssetsService.SnippetEdit { Name = " ", Markdown = "x" }));
        await Assert.ThrowsAsync<CmsException>(() => admin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit { Name = "空範本", Markdown = " " }));
        await Assert.ThrowsAsync<CmsException>(() => admin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "指向不存在的類型", Markdown = "x", ContentTypeId = Guid.NewGuid()
        }));

        await admin.Writing.DeleteTemplateAsync(pageTemplate.Id, pageTemplate.Version);
        await admin.Writing.DeleteSnippetAsync(snippet.Id, snippet.Version);
        Assert.Empty(await admin.Writing.ListSnippetsAsync());
        Assert.Equal("每月摘要（更新）", Assert.Single(await admin.Writing.ListTemplatesAsync()).Name);
    }

    [PostgresFact]
    public async Task Readers_can_list_but_cannot_change_writing_assets()
    {
        using var admin = await App.AdministratorAsync();
        await admin.Writing.SaveSnippetAsync(new WritingAssetsService.SnippetEdit { Name = "唯讀可見", Markdown = "內容" });
        var reader = await CreateMemberAsync(SiteRole.Reader);
        using var readerActor = await App.UserAsync(reader.Id);
        Assert.Single(await readerActor.Writing.ListSnippetsAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => readerActor.Writing.SaveSnippetAsync(
            new WritingAssetsService.SnippetEdit { Name = "越權", Markdown = "x" }));
    }

    [PostgresFact]
    public async Task Templates_and_snippets_survive_site_export_and_import()
    {
        using var sourceAdmin = await App.AdministratorAsync();
        var types = sourceAdmin.Services.GetRequiredService<ContentTypeService>();
        var sourceType = await types.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var postTemplate = await sourceAdmin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "每月摘要", Kind = ContentKind.Post, Title = "本月摘要",
            Markdown = "## 重點\n\n- \n\n## 下月計畫", Position = 1
        });
        var pageTemplate = await sourceAdmin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "著陸頁", Kind = ContentKind.Page, Markdown = "## 立即開始\n\n開始使用。", Position = 2
        });
        var typeTemplate = await sourceAdmin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "書評範本", ContentTypeId = sourceType.Id, Title = "書名與心得",
            Markdown = "## 書名\n\n## 心得", Position = 3
        });
        var snippet = await sourceAdmin.Writing.SaveSnippetAsync(new WritingAssetsService.SnippetEdit
        {
            Name = "版權聲明", Markdown = "> 未經授權請勿轉載。", Position = 5
        });

        using var buffer = new MemoryStream();
        await sourceAdmin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        Assert.Equal(3, (int?)document["version"]);
        Assert.Equal(3, document["templates"]!.AsArray().Count);
        Assert.Single(document["snippets"]!.AsArray());

        var site = await CreateSiteAsync();
        using var targetAdmin = await App.AdministratorAsync(site.Id);
        await targetAdmin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "目標站舊範本", Markdown = "舊內容"
        });
        await targetAdmin.Writing.SaveSnippetAsync(new WritingAssetsService.SnippetEdit
        {
            Name = "目標站舊片段", Markdown = "舊片段"
        });
        using var input = new MemoryStream(buffer.ToArray());
        await targetAdmin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        var restoredTemplates = await targetAdmin.Writing.ListTemplatesAsync();
        Assert.Equal(3, restoredTemplates.Count);
        var restoredPost = restoredTemplates.Single(x => x.Name == postTemplate.Name);
        Assert.Equal(postTemplate.Kind, restoredPost.Kind);
        Assert.Null(restoredPost.ContentTypeId);
        Assert.Equal(postTemplate.Title, restoredPost.Title);
        Assert.Equal(postTemplate.Markdown, restoredPost.Markdown);
        Assert.Equal(postTemplate.Position, restoredPost.Position);
        Assert.NotEqual(postTemplate.Id, restoredPost.Id);

        var restoredPage = restoredTemplates.Single(x => x.Name == pageTemplate.Name);
        Assert.Equal(pageTemplate.Kind, restoredPage.Kind);
        Assert.Null(restoredPage.ContentTypeId);
        Assert.Equal(pageTemplate.Title, restoredPage.Title);
        Assert.Equal(pageTemplate.Markdown, restoredPage.Markdown);
        Assert.Equal(pageTemplate.Position, restoredPage.Position);

        var restoredTypeTemplate = restoredTemplates.Single(x => x.Name == typeTemplate.Name);
        var targetTypes = targetAdmin.Services.GetRequiredService<ContentTypeService>();
        var restoredType = Assert.Single(await targetTypes.ListAsync(), x => x.Slug == sourceType.Slug);
        Assert.Equal(ContentKind.Post, restoredTypeTemplate.Kind);
        Assert.Equal(restoredType.Id, restoredTypeTemplate.ContentTypeId);
        Assert.Equal(typeTemplate.Title, restoredTypeTemplate.Title);
        Assert.Equal(typeTemplate.Markdown, restoredTypeTemplate.Markdown);
        Assert.Equal(typeTemplate.Position, restoredTypeTemplate.Position);
        Assert.NotEqual(sourceType.Id, restoredType.Id);

        var restoredSnippet = Assert.Single(await targetAdmin.Writing.ListSnippetsAsync());
        Assert.Equal(snippet.Name, restoredSnippet.Name);
        Assert.Equal(snippet.Markdown, restoredSnippet.Markdown);
        Assert.Equal(snippet.Position, restoredSnippet.Position);
        Assert.NotEqual(snippet.Id, restoredSnippet.Id);
        Assert.DoesNotContain(restoredTemplates, x => x.Name == "目標站舊範本");
        Assert.Equal(3, (await sourceAdmin.Writing.ListTemplatesAsync()).Count);
        Assert.Single(await sourceAdmin.Writing.ListSnippetsAsync());
    }

    [PostgresFact]
    public async Task Legacy_export_without_writing_asset_nodes_still_imports()
    {
        using var admin = await App.AdministratorAsync();
        var template = await admin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "既有範本", Markdown = "保留這段"
        });
        var snippet = await admin.Writing.SaveSnippetAsync(new WritingAssetsService.SnippetEdit
        {
            Name = "既有片段", Markdown = "保留片段"
        });
        var content = await admin.Content.SaveAsync(NewContent("舊格式文章", status: ContentStatus.Published));

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        document["version"] = 2;
        document.AsObject().Remove("templates");
        document.AsObject().Remove("snippets");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        var restoredTemplate = Assert.Single(await admin.Writing.ListTemplatesAsync());
        Assert.Equal(template.Id, restoredTemplate.Id);
        Assert.Equal(template.Markdown, restoredTemplate.Markdown);
        var restoredSnippet = Assert.Single(await admin.Writing.ListSnippetsAsync());
        Assert.Equal(snippet.Id, restoredSnippet.Id);
        Assert.Equal(snippet.Markdown, restoredSnippet.Markdown);
        await using var db = await App.CreateContextAsync();
        Assert.True(await db.Contents.AsNoTracking().AnyAsync(x => x.Slug == content.Slug && x.Status == ContentStatus.Published));
    }

    [PostgresFact]
    public async Task Template_for_a_custom_type_is_removed_with_the_type()
    {
        using var admin = await App.AdministratorAsync();
        var types = admin.Services.GetRequiredService<ContentTypeService>();
        var type = await types.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        await admin.Writing.SaveTemplateAsync(new WritingAssetsService.TemplateEdit
        {
            Name = "書評範本", ContentTypeId = type.Id, Markdown = "## 書名\n\n## 心得\n\n"
        });
        Assert.Single(await admin.Writing.ListTemplatesAsync());
        await types.DeleteAsync(type.Id, type.Version);
        Assert.Empty(await admin.Writing.ListTemplatesAsync());
    }
}
