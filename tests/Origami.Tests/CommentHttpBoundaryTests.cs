namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CommentHttpBoundaryTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    [PostgresFact]
    public async Task Visitor_comment_post_accepts_only_bff_not_anonymous_or_external_credentials()
    {
        using var admin = await App.AdministratorAsync();
        var settings = await admin.Settings.GetAsync();
        settings.CommentsEnabled = true;
        await admin.Settings.SaveAsync(settings);
        var post = await admin.Content.SaveAsync(NewContent("可評論文章", status: ContentStatus.Published));
        var path = $"/api/v1/admin/contents/{post.Id}/comments";
        var input = new CommentService.CommentPostInput("外部訪客", "https://example.test/", "留言", false);
        var serviceToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: serviceToken);

        using var anonymous = api.CreateClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync(path, input);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        foreach (var scope in new[] { ApiCredentialScope.Read, ApiCredentialScope.Write })
        {
            var credential = await admin.Credentials.CreateAsync(new CredentialCreateInput
            {
                Name = "Comments " + scope, Scope = scope, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            });
            using var external = api.CreateCredentialClient(credential.Token, SiteDefaults.DefaultSiteId);
            // 外部憑證即使偽造 BFF 的使用者轉發標頭，也不能進入訪客張貼入口。
            external.DefaultRequestHeaders.Add(AccessClaims.UserHeader, await App.AdminUserIdAsync());
            external.DefaultRequestHeaders.Add(AccessClaims.StampHeader, await App.AdminSecurityStampAsync());
            using var allowed = await external.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            using var denied = await external.PostAsJsonAsync(path, input);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        // BFF 代表匿名訪客張貼，不需要轉發管理員身分。
        using var bff = api.CreateClient(SiteDefaults.DefaultSiteId);
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        using var accepted = await bff.PostAsJsonAsync(path, input with { AuthorName = "BFF 訪客" });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        await using var db = await App.CreateContextAsync();
        var stored = Assert.Single(await db.Comments.AsNoTracking().ToListAsync());
        Assert.Equal(post.Id!.Value, stored.ContentId);
        Assert.Equal("BFF 訪客", stored.AuthorName);
        Assert.Equal(input.Url, stored.Url);
        Assert.Equal(CommentStatus.Pending, stored.Status);
    }
}
