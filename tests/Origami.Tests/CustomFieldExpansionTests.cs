namespace Origami.Tests;

/// <summary>自訂欄位擴充：內建文章／頁面可宣告欄位、新欄位種類的驗證、關聯目標的公開解析。</summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomFieldExpansionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static ContentTypeService Types(TestActor actor) => actor.Services.GetRequiredService<ContentTypeService>();

    private static ContentTypeService.FieldEdit Field(ContentKind kind, string name, string key, string type,
        bool required = false, string options = "", int position = 0, bool archived = false) => new()
    {
        BuiltinKind = kind, Name = name, Key = key, FieldType = type, IsRequired = required,
        Options = options, Position = position, IsArchived = archived
    };

    [PostgresFact]
    public async Task Builtin_posts_and_pages_declare_fields_and_values_are_enforced()
    {
        using var admin = await App.AdministratorAsync();
        var types = Types(admin);
        var source = await types.SaveFieldAsync(Field(ContentKind.Post, "來源", "source", CustomFieldTypes.Text, required: true));
        var email = await types.SaveFieldAsync(Field(ContentKind.Post, "聯絡信箱", "contact-email", CustomFieldTypes.Email));
        var pageNote = await types.SaveFieldAsync(Field(ContentKind.Page, "頁面備註", "source", CustomFieldTypes.LongText));
        Assert.Equal(ContentKind.Post, source.BuiltinKind);
        Assert.Null(source.ContentTypeId);
        Assert.Equal(2, (await types.ListBuiltinFieldsAsync(ContentKind.Post)).Count);

        // 同一種內建內容內鍵值唯一；文章與頁面互不相干。
        await Assert.ThrowsAsync<CmsException>(() => types.SaveFieldAsync(Field(ContentKind.Post, "重複鍵", "source", CustomFieldTypes.Text)));
        // 欄位必須恰屬於一個內容類型或一種內建內容。
        await Assert.ThrowsAsync<CmsException>(() => types.SaveFieldAsync(new ContentTypeService.FieldEdit { Name = "無歸屬", Key = "orphan" }));
        await Assert.ThrowsAsync<CmsException>(() => types.SaveFieldAsync(Field((ContentKind)7, "非法", "invalid", CustomFieldTypes.Text)));

        // 必填欄位缺值被拒絕；合法值儲存後可讀回。
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(NewContent("缺必填欄位")));
        var post = NewContent("有欄位的文章", status: ContentStatus.Published);
        post.FieldValues = [new(source.Id, "官方報導"), new(email.Id, "editor@example.com")];
        var saved = await admin.Content.SaveAsync(post);
        Assert.Equal("editor@example.com", saved.FieldValues.Single(x => x.FieldId == email.Id).Value);
        var detail = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal("官方報導", detail.FieldValues.Single(x => x.FieldId == source.Id).Value);

        // 頁面使用自己宣告的同鍵欄位；文章不能填頁面的欄位。
        var page = NewContent("有欄位的頁面", kind: ContentKind.Page);
        page.FieldValues = [new(pageNote.Id, "備註內容")];
        await admin.Content.SaveAsync(page);
        var wrong = NewContent("用錯欄位的文章");
        wrong.FieldValues = [new(source.Id, "x"), new(pageNote.Id, "y")];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(wrong));
    }

    [PostgresFact]
    public async Task New_field_types_are_validated_and_relations_resolve_to_published_targets()
    {
        using var admin = await App.AdministratorAsync();
        var types = Types(admin);
        var gallery = await types.SaveFieldAsync(Field(ContentKind.Post, "圖庫", "gallery", CustomFieldTypes.Gallery));
        var when = await types.SaveFieldAsync(Field(ContentKind.Post, "時間", "when", CustomFieldTypes.DateTimeField));
        var related = await types.SaveFieldAsync(Field(ContentKind.Post, "相關內容", "related", CustomFieldTypes.Relation));
        var contact = await types.SaveFieldAsync(Field(ContentKind.Post, "信箱", "email", CustomFieldTypes.Email));

        var target = await admin.Content.SaveAsync(NewContent("關聯目標文章", status: ContentStatus.Published));
        var draft = await admin.Content.SaveAsync(NewContent("關聯草稿目標"));
        var post = NewContent("帶關聯的文章", status: ContentStatus.Published);
        post.FieldValues =
        [
            new(related.Id, target.Id + "\n" + draft.Id), new(when.Id, "2026-11-23T09:30"),
            new(contact.Id, "someone@example.org"), new(gallery.Id, "/media/" + new string('a', 32) + ".png")
        ];
        var saved = await admin.Content.SaveAsync(post);
        var published = await admin.Content.WithRelationTargetsAsync(
            await admin.Content.GetPublishedAsync(ContentKind.Post, saved.Slug ?? ""));
        Assert.NotNull(published);
        var relationField = published.Fields.Single(f => f.Key == "related");
        // 只有已發布目標露出，並附上正式網址。
        var link = Assert.Single(relationField.Targets);
        Assert.Equal("關聯目標文章", link.Title);
        Assert.EndsWith("/posts/" + Uri.EscapeDataString(target.Slug ?? ""), link.Path, StringComparison.Ordinal);
        Assert.Empty(published.Fields.Single(f => f.Key == "email").Targets);

        foreach (var (fieldId, value) in new (Guid, string)[]
                 {
                     (contact.Id, "not-an-email"), (when.Id, "2026-11-23 09:30"),
                     (gallery.Id, "https://evil.example/a.png"), (related.Id, Guid.NewGuid().ToString()), (related.Id, "not-a-guid")
                 })
        {
            var invalid = NewContent("無效欄位值");
            invalid.FieldValues = [new(fieldId, value)];
            await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(invalid));
        }
    }

    [PostgresFact]
    public async Task Legacy_relation_values_are_remapped_on_same_site_import()
    {
        using var admin = await App.AdministratorAsync();
        var relationField = await Types(admin).SaveFieldAsync(Field(
            ContentKind.Post, "相關內容", "related", CustomFieldTypes.Relation));
        var target = await admin.Content.SaveAsync(NewContent("舊格式關聯目標", status: ContentStatus.Published));
        var source = NewContent("舊格式關聯來源", status: ContentStatus.Published);
        source.FieldValues = [new(relationField.Id, target.Id!.Value.ToString())];
        var saved = await admin.Content.SaveAsync(source);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        document["version"] = 2;
        var exported = document["contents"]!.AsArray().Single(x => (string?)x!["slug"] == saved.Slug)!;
        exported["fieldValues"] = new JsonObject { ["related"] = target.Id!.Value.ToString() };
        exported.AsObject().Remove("fieldRelations");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var db = await App.CreateContextAsync();
        var restoredSource = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == saved.Slug);
        var restoredTarget = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == target.Slug);
        var value = await db.ContentFieldValues.AsNoTracking().Where(x => x.ContentId == restoredSource.Id)
            .Select(x => x.Value).SingleAsync();
        Assert.NotEqual(target.Id, restoredTarget.Id);
        Assert.Equal(restoredTarget.Id.ToString(), value);
        var published = await admin.Content.WithRelationTargetsAsync(
            await admin.Content.GetPublishedAsync(ContentKind.Post, restoredSource.Slug));
        Assert.NotNull(published);
        Assert.Equal("舊格式關聯目標", Assert.Single(published.Fields.Single(x => x.Key == "related").Targets).Title);
    }

    [PostgresFact]
    public async Task Builtin_fields_values_and_relations_survive_site_export_and_import()
    {
        using var sourceAdmin = await App.AdministratorAsync();
        var sourceField = await Types(sourceAdmin).SaveFieldAsync(Field(
            ContentKind.Post, "來源", "source", CustomFieldTypes.Select,
            required: true, options: "內部,外部", position: 4));
        var pageField = await Types(sourceAdmin).SaveFieldAsync(Field(
            ContentKind.Page, "頁面備註", "source", CustomFieldTypes.LongText,
            position: 7, archived: true));
        var relationField = await Types(sourceAdmin).SaveFieldAsync(Field(
            ContentKind.Post, "相關內容", "related", CustomFieldTypes.Relation, position: 2));

        var publishedTarget = NewContent("已發布關聯目標", status: ContentStatus.Published);
        publishedTarget.FieldValues = [new(sourceField.Id, "外部")];
        var savedTarget = await sourceAdmin.Content.SaveAsync(publishedTarget);
        var draftTarget = NewContent("草稿關聯目標");
        draftTarget.FieldValues = [new(sourceField.Id, "外部")];
        var savedDraftTarget = await sourceAdmin.Content.SaveAsync(draftTarget);

        var sourcePost = NewContent("帶自訂欄位的文章", status: ContentStatus.Published);
        sourcePost.FieldValues =
        [
            new(sourceField.Id, "內部"),
            new(relationField.Id, savedTarget.Id + "\n" + savedDraftTarget.Id)
        ];
        var savedPost = await sourceAdmin.Content.SaveAsync(sourcePost);
        var sourcePage = NewContent("帶自訂欄位的頁面", kind: ContentKind.Page, status: ContentStatus.Published);
        sourcePage.FieldValues = [new(pageField.Id, "頁面欄位值")];
        var savedPage = await sourceAdmin.Content.SaveAsync(sourcePage);

        using var buffer = new MemoryStream();
        await sourceAdmin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var export = buffer.ToArray();
        var document = JsonNode.Parse(export)!;
        Assert.Equal(3, (int?)document["version"]);
        var exportedPost = document["contents"]!.AsArray().Single(x => (string?)x!["slug"] == savedPost.Slug)!;
        Assert.Equal(savedTarget.Slug, (string?)exportedPost["fieldRelations"]!["related"]![0]);
        Assert.Equal(savedDraftTarget.Slug, (string?)exportedPost["fieldRelations"]!["related"]![1]);
        Assert.Null(exportedPost["fieldValues"]!["related"]);
        Assert.DoesNotContain(savedTarget.Id!.Value.ToString(), Encoding.UTF8.GetString(export), StringComparison.Ordinal);

        var site = await CreateSiteAsync();
        using var targetAdmin = await App.AdministratorAsync(site.Id);
        using var input = new MemoryStream(export);
        await targetAdmin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        var postFields = (await Types(targetAdmin).ListBuiltinFieldsAsync(ContentKind.Post))
            .ToDictionary(x => x.Key, StringComparer.Ordinal);
        Assert.Equal(2, postFields.Count);
        var restoredSource = postFields["source"];
        Assert.Equal(sourceField.Name, restoredSource.Name);
        Assert.Equal(sourceField.FieldType, restoredSource.FieldType);
        Assert.Equal(sourceField.Options, restoredSource.Options);
        Assert.True(restoredSource.IsRequired);
        Assert.Equal(sourceField.Position, restoredSource.Position);
        Assert.False(restoredSource.IsArchived);
        Assert.Equal(ContentKind.Post, restoredSource.BuiltinKind);
        Assert.Null(restoredSource.ContentTypeId);
        Assert.Equal(relationField.FieldType, postFields["related"].FieldType);

        var restoredPageField = Assert.Single(await Types(targetAdmin).ListBuiltinFieldsAsync(ContentKind.Page));
        Assert.Equal(pageField.Name, restoredPageField.Name);
        Assert.Equal(pageField.Key, restoredPageField.Key);
        Assert.Equal(pageField.FieldType, restoredPageField.FieldType);
        Assert.True(restoredPageField.IsArchived);
        Assert.Equal(pageField.Position, restoredPageField.Position);
        Assert.Equal(ContentKind.Page, restoredPageField.BuiltinKind);

        await using var db = await App.CreateContextAsync(site.Id);
        var restoredPost = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == savedPost.Slug);
        var restoredPage = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == savedPage.Slug);
        var restoredTarget = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == savedTarget.Slug);
        var restoredDraft = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == savedDraftTarget.Slug);
        Assert.NotEqual(savedPost.Id, restoredPost.Id);
        Assert.NotEqual(savedPage.Id, restoredPage.Id);
        Assert.NotEqual(savedTarget.Id, restoredTarget.Id);

        var postValues = await db.ContentFieldValues.AsNoTracking().Include(x => x.Field)
            .Where(x => x.ContentId == restoredPost.Id)
            .ToDictionaryAsync(x => x.Field.Key, x => x.Value);
        Assert.Equal("內部", postValues["source"]);
        Assert.Equal($"{restoredTarget.Id}\n{restoredDraft.Id}", postValues["related"]);
        var pageValue = await db.ContentFieldValues.AsNoTracking().Where(x => x.ContentId == restoredPage.Id)
            .Select(x => x.Value).SingleAsync();
        Assert.Equal("頁面欄位值", pageValue);

        var edit = await targetAdmin.Content.GetEditAsync(restoredPost.Id);
        Assert.Equal(postValues["source"], edit.FieldValues.Single(x => x.FieldId == restoredSource.Id).Value);
        Assert.Equal(postValues["related"], edit.FieldValues.Single(x => x.FieldId == postFields["related"].Id).Value);
        var published = await targetAdmin.Content.WithRelationTargetsAsync(
            await targetAdmin.Content.GetPublishedAsync(ContentKind.Post, restoredPost.Slug));
        Assert.NotNull(published);
        var relation = published.Fields.Single(x => x.Key == "related");
        var link = Assert.Single(relation.Targets);
        Assert.Equal("已發布關聯目標", link.Title);
        Assert.EndsWith("/posts/" + Uri.EscapeDataString(restoredTarget.Slug), link.Path, StringComparison.Ordinal);

        var publishedPage = await targetAdmin.Content.WithRelationTargetsAsync(
            await targetAdmin.Content.GetPublishedAsync(ContentKind.Page, restoredPage.Slug));
        Assert.NotNull(publishedPage);
        Assert.Equal("頁面欄位值", publishedPage.Fields.Single(x => x.Key == "source").Value);
    }
}
