namespace Origami.Tests;

/// <summary>
/// 內容作者歸屬：建立時蓋作者戳記（帳號識別＋顯示名稱快照）、更新不重寫、
/// 成員改名同步既有內容署名（僅當前站點）、匯出匯入保留署名快照。
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentAuthorTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Creating_content_stamps_the_current_member_as_author()
    {
        var user = await CreateMemberAsync(SiteRole.Editor);
        using var admin = await App.AdministratorAsync();
        // 先為成員設定顯示名稱，再以成員身分建立內容。
        var member = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == user.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = member.Role, DisplayName = "單文浩", Version = member.Version
        });

        using var actor = await App.UserAsync(user.Id);
        var saved = await actor.Content.SaveAsync(NewContent("作者文章", status: ContentStatus.Published));

        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == saved.Id!.Value);
        Assert.Equal(user.Id, row.AuthorId);
        Assert.Equal("單文浩", row.AuthorName);

        // 後台清單與公開詳情都帶署名快照。
        var summary = (await actor.Content.ListAdminAsync(ContentKind.Post)).Items.Single(x => x.Id == saved.Id);
        Assert.Equal("單文浩", summary.AuthorName);
        var detail = await actor.Content.GetPublishedAsync(ContentKind.Post, saved.Slug!);
        Assert.NotNull(detail);
        Assert.Equal("單文浩", detail.AuthorName);
        var feed = await actor.Content.FeedAsync(10);
        Assert.Equal("單文浩", feed.Single(x => x.Id == saved.Id).AuthorName);
    }

    [PostgresFact]
    public async Task Updating_content_never_rewrites_the_original_author()
    {
        var user = await CreateMemberAsync(SiteRole.Editor);
        using var admin = await App.AdministratorAsync();
        var member = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == user.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = member.Role, DisplayName = "原作者", Version = member.Version
        });

        using var actor = await App.UserAsync(user.Id);
        var created = await actor.Content.SaveAsync(NewContent("被編輯的文章"));

        //另一位管理者編輯同一篇內容；作者歸屬不變。
        using var other = await App.AdministratorAsync();
        var edit = await other.Content.GetEditAsync(created.Id!.Value);
        edit.Markdown = "改由管理者編輯";
        await other.Content.SaveAsync(edit);

        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(user.Id, row.AuthorId);
        Assert.Equal("原作者", row.AuthorName);
    }

    [PostgresFact]
    public async Task Renaming_a_member_updates_their_content_byline_only_on_the_current_site()
    {
        var site2 = await CreateSiteAsync("second");
        var user = await CreateMemberAsync(SiteRole.Editor, site2.Id, "dual");
        // 同一位使用者也要加入預設站，才能在兩站分別建立內容。
        using var adminDefault = await App.AdministratorAsync();
        await adminDefault.Sites.SetMemberAsync(new MemberEditInput { UserName = "dual", Role = SiteRole.Editor });

        using (var actorDefault = await App.UserAsync(user.Id))
        {
            await actorDefault.Content.SaveAsync(NewContent("預設站文章", status: ContentStatus.Published));
        }
        using (var actorSecond = await App.UserAsync(user.Id, site2.Id))
        {
            await actorSecond.Content.SaveAsync(NewContent("第二站文章", status: ContentStatus.Published));
        }

        // 在第二站改名：只同步第二站的內容署名，預設站不受影響。
        using var adminSecond = await App.AdministratorAsync(site2.Id);
        var secondMember = (await adminSecond.Sites.ListMembersAsync()).Single(x => x.UserId == user.Id);
        await adminSecond.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = secondMember.UserName, Role = secondMember.Role, DisplayName = "第二站新名", Version = secondMember.Version
        });

        await using var db2 = await App.CreateContextAsync(site2.Id);
        var secondRow = await db2.Contents.AsNoTracking().SingleAsync(x => x.Slug == "第二站文章");
        Assert.Equal("第二站新名", secondRow.AuthorName);
        await using var db1 = await App.CreateContextAsync();
        var defaultRow = await db1.Contents.AsNoTracking().SingleAsync(x => x.Slug == "預設站文章");
        Assert.NotEqual("第二站新名", defaultRow.AuthorName);
    }

    [PostgresFact]
    public async Task Unattributed_content_falls_back_to_site_settings_byline()
    {
        // 全站管理者（無站點顯示名稱）建立的內容署名為空，由前台退回站點設定。
        using var admin = await App.AdministratorAsync();
        var saved = await admin.Content.SaveAsync(NewContent("無署名文章", status: ContentStatus.Published));
        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Id == saved.Id!.Value);
        Assert.Equal("", row.AuthorName);
        var detail = await admin.Content.GetPublishedAsync(ContentKind.Post, saved.Slug!);
        Assert.NotNull(detail);
        Assert.Equal("", detail.AuthorName);
    }

    [PostgresFact]
    public async Task Export_and_import_preserve_the_author_byline_snapshot()
    {
        var user = await CreateMemberAsync(SiteRole.Editor);
        using var admin = await App.AdministratorAsync();
        var member = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == user.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = member.Role, DisplayName = "署名作者", Version = member.Version
        });
        using var actor = await App.UserAsync(user.Id);
        var saved = await actor.Content.SaveAsync(NewContent("署名匯出文章", status: ContentStatus.Published));

        using var buffer = new MemoryStream();
        await admin.Services.GetRequiredService<ExportService>().WriteAsync(buffer);
        var document = JsonNode.Parse(buffer.ToArray())!;
        Assert.Equal("署名作者", document["contents"]!.AsArray().Single(x => x!["slug"]!.GetValue<string>() == saved.Slug)!["authorName"]!.GetValue<string>());

        var importer = admin.Services.GetRequiredService<ImportService>();
        using (var input = new MemoryStream(buffer.ToArray()))
        {
            await importer.ApplyAsync(input, confirmed: true);
        }

        // 匯入後帳號不存在於檔案中，署名快照保留、帳號參照清空。
        await using var db = await App.CreateContextAsync();
        var row = await db.Contents.AsNoTracking().SingleAsync(x => x.Slug == saved.Slug);
        Assert.Equal("署名作者", row.AuthorName);
        Assert.Null(row.AuthorId);
    }

    [PostgresFact]
    public async Task Display_name_is_trimmed_and_bounded()
    {
        var user = await CreateMemberAsync(SiteRole.Editor, name: "named");
        using var admin = await App.AdministratorAsync();
        var member = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == user.Id);
        // 前後空白會被修剪；過長名稱被拒絕。
        await admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = member.Role, DisplayName = "  修剪我  ", Version = member.Version
        });
        var renamed = (await admin.Sites.ListMembersAsync()).Single(x => x.UserId == user.Id);
        Assert.Equal("修剪我", renamed.DisplayName);
        await Assert.ThrowsAsync<CmsException>(() => admin.Sites.SetMemberAsync(new MemberEditInput
        {
            UserName = member.UserName, Role = member.Role,
            DisplayName = new string('長', Limits.AuthorName + 1), Version = renamed.Version
        }));
    }
}
