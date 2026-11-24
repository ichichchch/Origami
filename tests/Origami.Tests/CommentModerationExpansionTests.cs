namespace Origami.Tests;

/// <summary>評論擴充：批次審核、後台編輯（標示已編輯）、單篇評論 feed、巢狀顯示與記住我。</summary>
[Collection(PostgresCollection.Name)]
public sealed class CommentModerationExpansionTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static CommentService Comments(TestActor actor) => actor.Services.GetRequiredService<CommentService>();

    [PostgresFact]
    public async Task Bulk_moderation_and_editing_report_per_item_and_mark_edited()
    {
        using var admin = await App.AdministratorAsync();
        var comments = Comments(admin);
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        await admin.Settings.SaveAsync(settings);
        var post = await admin.Content.SaveAsync(NewContent("有評論的文章", status: ContentStatus.Published));
        var contentId = post.Id!.Value;
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("Alice", null, "第一則 **原文**", false));
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("Bob", null, "第二則", false));
        var pending = (await comments.ListAdminAsync(CommentStatus.Pending)).Items;
        Assert.Equal(2, pending.Count);

        // 批次核可：逐筆攜帶版本；過期版本的那一筆失敗，另一筆照常成功。
        var results = await comments.BulkModerateAsync([(pending[0].Id, pending[0].Version), (pending[1].Id, Guid.NewGuid())], CommentStatus.Approved);
        Assert.True(results[0].Succeeded, results[0].Message);
        Assert.False(results[1].Succeeded);
        Assert.Equal(CommentStatus.Approved, (await comments.ListAdminAsync(CommentStatus.Approved)).Items.Single(x => x.Id == pending[0].Id).Status);
        await Assert.ThrowsAsync<CmsException>(() => comments.BulkModerateAsync([], CommentStatus.Spam));

        // 編輯訪客評論：內容更新並標示已編輯；過期版本被拒絕。
        var approved = (await comments.ListAdminAsync(CommentStatus.Approved)).Items.Single(x => x.Id == pending[0].Id);
        var edited = await comments.UpdateAsync(approved.Id, "第一則 **修訂後**", approved.Version);
        Assert.True(edited.IsEdited);
        Assert.Equal("第一則 **修訂後**", edited.Markdown);
        await Assert.ThrowsAsync<CmsException>(() => comments.UpdateAsync(approved.Id, "再次修改", approved.Version));
        var publicView = (await comments.ListPublicAsync(contentId)).Single();
        Assert.True(publicView.IsEdited);
        Assert.Equal("第一則 **修訂後**", publicView.Markdown);

        // 官方回覆不可編輯。
        var reply = await comments.ReplyAdminAsync(approved.Id, "官方回覆內容");
        await Assert.ThrowsAsync<CmsException>(() => comments.UpdateAsync(reply.Id, "改寫官方回覆", reply.Version));
    }

    [PostgresFact]
    public async Task Per_post_comment_feed_nesting_and_remember_me_work_over_http()
    {
        using var admin = await App.AdministratorAsync();
        var comments = Comments(admin);
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        await admin.Settings.SaveAsync(settings);
        var post = await admin.Content.SaveAsync(NewContent("有討論的文章", status: ContentStatus.Published));
        var other = await admin.Content.SaveAsync(NewContent("另一篇文章", status: ContentStatus.Published));
        await comments.PostAsync(post.Id!.Value, new CommentService.CommentPostInput("Alice", null, "主評論", false));
        await comments.PostAsync(post.Id!.Value, new CommentService.CommentPostInput("Bob", null, "回覆內容", false));
        await comments.PostAsync(other.Id!.Value, new CommentService.CommentPostInput("Carol", null, "別篇的評論", false));
        foreach (var item in (await comments.ListAdminAsync(null)).Items)
            await comments.ModerateAsync(item.Id, CommentStatus.Approved, item.Version);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        var cookieJar = new CookieContainer();
        using var client = site.CreateClient(cookies: cookieJar);

        // 單篇評論 feed：只含該篇；不存在的文章回 404。
        using var feed = await client.GetAsync("/feed/comments?post=" + Uri.EscapeDataString(post.Slug ?? ""));
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        var feedXml = await feed.Content.ReadAsStringAsync();
        Assert.Contains("主評論", feedXml, StringComparison.Ordinal);
        Assert.Contains("回覆內容", feedXml, StringComparison.Ordinal);
        Assert.DoesNotContain("別篇的評論", feedXml, StringComparison.Ordinal);
        using var missingFeed = await client.GetAsync("/feed/comments?post=does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, missingFeed.StatusCode);
        Assert.Equal(2, (await comments.FeedCommentsAsync(10, contentId: post.Id)).Count);

        // 記住我：表單 POST 成功後以 Cookie 記住名稱與網址（不含電子郵件）。
        var pageHtml = await client.GetStringAsync("/posts/" + Uri.EscapeDataString(post.Slug ?? ""));
        Assert.Contains("feed/comments?post=", pageHtml, StringComparison.Ordinal);
        var fields = new Dictionary<string, string>();
        foreach (var input in pageHtml.Split("<input", StringSplitOptions.RemoveEmptyEntries))
        {
            var nameMatch = System.Text.RegularExpressions.Regex.Match(input, "name=\"([^\"]+)\"");
            var valueMatch = System.Text.RegularExpressions.Regex.Match(input, "value=\"([^\"]*)\"");
            if (!nameMatch.Success || !valueMatch.Success) continue;
            var name = nameMatch.Groups[1].Value;
            var tagEnd = input.IndexOf('>');
            var tag = input[..(tagEnd >= 0 ? tagEnd : input.Length)];
            if (name == "CommentForm.NotifyReplies" && !tag.Contains(" checked", StringComparison.OrdinalIgnoreCase)) continue;
            fields[name] = WebUtility.HtmlDecode(valueMatch.Groups[1].Value);
        }
        Assert.True(fields.ContainsKey("__RequestVerificationToken"));
        fields["CommentForm.AuthorName"] = "Dora";
        fields["CommentForm.Url"] = "https://dora.example.com";
        fields["CommentForm.Body"] = "透過表單的評論";
        fields["CommentForm.Remember"] = "true";
        using var posted = await client.PostAsync("/posts/" + Uri.EscapeDataString(post.Slug ?? ""), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var postedHtml = WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync());
        Assert.True(postedHtml.Contains("評論已送出", StringComparison.Ordinal),
            $"Web exceptions: {string.Join(", ", site.WarningExceptionTypes)}; API exceptions: {string.Join(", ", site.ApiErrorTypes)}");
        var setCookie = posted.Headers.TryGetValues("Set-Cookie", out var cookies) ? string.Join("|", cookies) : "";
        Assert.True(setCookie.Contains("origami.commenter", StringComparison.Ordinal));
        Assert.True(cookieJar.GetCookies(site.Address)["origami.commenter"] is not null);
        Assert.DoesNotContain("@", setCookie, StringComparison.Ordinal);
        using var again = await client.GetAsync("/posts/" + Uri.EscapeDataString(post.Slug ?? ""));
        var againHtml = await again.Content.ReadAsStringAsync();
        Assert.Contains("value=\"Dora\"", againHtml, StringComparison.Ordinal);
        Assert.Contains("https://dora.example.com", againHtml, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task Replies_are_indented_up_to_the_configured_depth()
    {
        using var admin = await App.AdministratorAsync();
        var comments = Comments(admin);
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        await admin.Settings.SaveAsync(settings);
        var post = await admin.Content.SaveAsync(NewContent("巢狀評論文章", status: ContentStatus.Published));
        var contentId = post.Id!.Value;
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("Alice", null, "根", false));
        var root = (await comments.ListAdminAsync(CommentStatus.Pending)).Items.Single();
        await comments.ModerateAsync(root.Id, CommentStatus.Approved, root.Version);
        await comments.PostAsync(contentId, new CommentService.CommentPostInput("Bob", null, "回覆", Honeypot: false, ParentId: root.Id));
        var reply = (await comments.ListAdminAsync(CommentStatus.Pending)).Items.Single();
        await comments.ModerateAsync(reply.Id, CommentStatus.Approved, reply.Version);

        await using var site = new RunningSite(App.ConnectionString);
        await site.StartAsync();
        using var client = site.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/posts/" + Uri.EscapeDataString(post.Slug ?? "")));
        // 回覆依深度縮排（站點預設深度 3 → 第 1 層縮排 1.4rem）。
        Assert.Contains("margin-inline-start:1.4rem", html, StringComparison.Ordinal);
    }
}
