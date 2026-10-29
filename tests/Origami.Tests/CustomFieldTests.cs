namespace Origami.Tests;

/// <summary>
/// 宣告式擴充點（自訂欄位）：欄位宣告的站點隔離、內容值的驗證與型別限制、
/// 公開呈現，以及匯出匯入往返。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomFieldTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static ContentTypeService TypeService(TestActor actor) =>
        actor.Services.GetRequiredService<ContentTypeService>();

    [PostgresFact]
    public async Task Fields_are_declared_per_content_type_and_values_are_validated()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var other = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "隨筆", Slug = "essays" });
        var field = await service.SaveFieldAsync(new ContentTypeService.FieldEdit
        {
            ContentTypeId = type.Id, Name = "出版社", Key = "publisher", FieldType = CustomFieldTypes.Text
        });
        Assert.Equal("publisher", field.Key);
        Assert.Single(await service.ListFieldsAsync(type.Id));
        Assert.Empty(await service.ListFieldsAsync(other.Id));

        // 同一內容類型不可有重複鍵值。
        await Assert.ThrowsAsync<CmsException>(() => service.SaveFieldAsync(new ContentTypeService.FieldEdit
        { ContentTypeId = type.Id, Name = "出版社二", Key = "publisher" }));

        var content = NewContent("書評文章", status: ContentStatus.Draft);
        content.ContentTypeId = type.Id;
        content.FieldValues = [new(field.Id, "某某出版社")];
        var saved = await admin.Content.SaveAsync(content);
        Assert.Equal("某某出版社", Assert.Single(saved.FieldValues).Value);

        // 欄位種類不符時拒絕。
        var numeric = await service.SaveFieldAsync(new ContentTypeService.FieldEdit
        { ContentTypeId = type.Id, Name = "頁數", Key = "pages", FieldType = CustomFieldTypes.Number });
        var wrong = await admin.Content.GetEditAsync(saved.Id!.Value);
        wrong.FieldValues = [new(numeric.Id, "不是數字")];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(wrong));

        // 別的內容類型的欄位不可指派；內建文章不可使用自訂欄位。
        var foreign = await service.SaveFieldAsync(new ContentTypeService.FieldEdit
        { ContentTypeId = other.Id, Name = "主題", Key = "topic", FieldType = CustomFieldTypes.Text });
        var foreignUse = await admin.Content.GetEditAsync(saved.Id.Value);
        foreignUse.FieldValues = [new(foreign.Id, "跨類型")];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.UpdateAsync(foreignUse));

        var plain = NewContent("一般文章", status: ContentStatus.Draft);
        plain.FieldValues = [new(field.Id, "不該出現")];
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(plain));
    }

    [PostgresFact]
    public async Task Required_fields_must_have_a_value_and_values_survive_a_reload()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var field = await service.SaveFieldAsync(new ContentTypeService.FieldEdit
        {
            ContentTypeId = type.Id, Name = "出版社", Key = "publisher",
            FieldType = CustomFieldTypes.Text, IsRequired = true
        });
        var content = NewContent("書評文章", status: ContentStatus.Draft);
        content.ContentTypeId = type.Id;
        await Assert.ThrowsAsync<CmsException>(() => admin.Content.SaveAsync(content));

        content.FieldValues = [new(field.Id, "某某出版社")];
        var saved = await admin.Content.SaveAsync(content);
        var reloaded = await admin.Content.GetEditAsync(saved.Id!.Value);
        Assert.Equal("某某出版社", Assert.Single(reloaded.FieldValues).Value);
    }

    [PostgresFact]
    public async Task Published_content_exposes_its_field_values_publicly()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var publisher = await service.SaveFieldAsync(new ContentTypeService.FieldEdit
        { ContentTypeId = type.Id, Name = "出版社", Key = "publisher", FieldType = CustomFieldTypes.Text, Position = 1 });
        var featured = await service.SaveFieldAsync(new ContentTypeService.FieldEdit
        { ContentTypeId = type.Id, Name = "精選", Key = "featured", FieldType = CustomFieldTypes.Checkbox, Position = 2 });
        var content = NewContent("書評文章", status: ContentStatus.Published);
        content.ContentTypeId = type.Id;
        content.FieldValues = [new(publisher.Id, "某某出版社"), new(featured.Id, "true")];
        await admin.Content.SaveAsync(content);

        var detail = await App.Anonymous().Content.GetPublishedByTypeAsync("reviews", content.Slug!);
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Fields.Count);
        Assert.Equal("publisher", detail.Fields[0].Key);
        Assert.Equal("某某出版社", detail.Fields[0].Value);
        Assert.Equal("true", detail.Fields[1].Value);
    }

    [PostgresFact]
    public async Task Fields_and_values_survive_the_export_and_import_round_trip()
    {
        using var admin = await App.AdministratorAsync();
        var service = TypeService(admin);
        var type = await service.SaveAsync(new ContentTypeService.TypeEdit { Name = "書評", Slug = "reviews" });
        var field = await service.SaveFieldAsync(new ContentTypeService.FieldEdit
        { ContentTypeId = type.Id, Name = "出版社", Key = "publisher", FieldType = CustomFieldTypes.Text });
        var content = NewContent("書評文章", status: ContentStatus.Published);
        content.ContentTypeId = type.Id;
        content.FieldValues = [new(field.Id, "某某出版社")];
        await admin.Content.SaveAsync(content);

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        using var input = new MemoryStream(buffer.ToArray());
        await admin.Services.GetRequiredService<ImportService>().ApplyAsync(input, confirmed: true);

        await using var db = await App.CreateContextAsync();
        var restoredField = await db.CustomFields.AsNoTracking().SingleAsync(x => x.Key == "publisher");
        Assert.Equal("出版社", restoredField.Name);
        var restoredContent = await db.Contents.AsNoTracking().SingleAsync(x => x.Title == "書評文章");
        var restoredValue = await db.ContentFieldValues.AsNoTracking()
            .SingleAsync(x => x.ContentId == restoredContent.Id && x.FieldId == restoredField.Id);
        Assert.Equal("某某出版社", restoredValue.Value);
    }
}
