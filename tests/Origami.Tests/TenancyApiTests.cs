namespace Origami.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TenancyApiTests(IsolatedPostgres postgres) : ServiceTestBase(postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [PostgresFact]
    public async Task Bff_uses_explicit_user_identity_and_returns_403_for_insufficient_site_roles()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var site = await CreateSiteAsync();
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: token);
        foreach (var role in new[] { SiteRole.Reader, SiteRole.Editor, SiteRole.Administrator })
        {
            var member = await CreateMemberAsync(role, site.Id, role.ToString().ToLowerInvariant());
            using var actor = await App.UserAsync(member.Id, site.Id);
            var user = (await actor.Users.FindByIdAsync(member.Id))!;
            using var client = api.CreateBffClient(token, member.Id, user.SecurityStamp!, site.Id);
            using var read = await client.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var write = await client.PostAsJsonAsync("/api/v1/admin/contents", NewContent(role + " post"), Json);
            Assert.Equal(role == SiteRole.Reader ? HttpStatusCode.Forbidden : HttpStatusCode.Created, write.StatusCode);
            using var settings = await client.GetAsync("/api/v1/admin/settings");
            Assert.Equal(role == SiteRole.Administrator ? HttpStatusCode.OK : HttpStatusCode.Forbidden, settings.StatusCode);
            using var users = await client.GetAsync("/api/v1/admin/users");
            Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
            using var networkWrite = await client.PostAsJsonAsync("/api/v1/admin/sites", new SiteCreateInput
            {
                Name = "Forbidden", Slug = "forbidden", HostName = "forbidden.example.test"
            });
            Assert.Equal(HttpStatusCode.Forbidden, networkWrite.StatusCode);
            using var otherSite = api.CreateBffClient(token, member.Id, user.SecurityStamp!, SiteDefaults.DefaultSiteId);
            using var foreign = await otherSite.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        }
        using var stampOnly = api.CreateClient(site.Id);
        stampOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        stampOnly.DefaultRequestHeaders.Add(AccessClaims.StampHeader, await App.AdminSecurityStampAsync());
        using var inferred = await stampOnly.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, inferred.StatusCode);
        using var wrongIdentity = api.CreateBffClient(token, "unknown-user", await App.AdminSecurityStampAsync(), site.Id);
        using var unknown = await wrongIdentity.GetAsync("/api/v1/admin/contents?kind=Post");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        await using var db = await App.CreateContextAsync(site.Id);
        Assert.Equal(2, await db.Contents.CountAsync());
        Assert.Equal(2, await db.Sites.CountAsync());
    }

    [PostgresFact]
    public async Task Bff_issues_hash_only_credentials_and_pat_read_write_is_site_bound()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var site = await CreateSiteAsync();
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: token);
        using var bff = api.CreateBffClient(token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync(), site.Id);
        foreach (var scope in new[] { ApiCredentialScope.Read, ApiCredentialScope.Write })
        {
            using var issued = await bff.PostAsJsonAsync("/api/v1/admin/credentials", new CredentialCreateInput
            {
                Name = scope.ToString(), Scope = scope, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            }, Json);
            Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
            Assert.True(issued.Headers.CacheControl?.NoStore);
            var created = await issued.Content.ReadFromJsonAsync<CreatedCredential>(Json);
            Assert.NotNull(created);
            Assert.Matches("\\Aorg_[0-9a-f]{32}\\.[0-9a-f]{64}\\z", created.Token);
            using var pat = api.CreateCredentialClient(created.Token, site.Id);
            using var read = await pat.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var write = await pat.PostAsJsonAsync("/api/v1/admin/contents", NewContent(scope + " PAT"), Json);
            Assert.Equal(scope == ApiCredentialScope.Read ? HttpStatusCode.Forbidden : HttpStatusCode.Created, write.StatusCode);
            using var settings = await pat.GetAsync("/api/v1/admin/settings");
            Assert.Equal(HttpStatusCode.Forbidden, settings.StatusCode);
            using var wrongSite = api.CreateCredentialClient(created.Token, SiteDefaults.DefaultSiteId);
            using var wrong = await wrongSite.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            using var publicWrong = await wrongSite.GetAsync("/api/v1/posts");
            Assert.Equal(HttpStatusCode.Unauthorized, publicWrong.StatusCode);
            using var list = await bff.GetAsync("/api/v1/admin/credentials");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var listed = await list.Content.ReadAsStringAsync();
            Assert.DoesNotContain(created.Token[37..], listed);
            Assert.DoesNotContain("secretHash", listed);
            await using (var db = await App.CreateContextAsync(site.Id))
            {
                var stored = await db.ApiCredentials.SingleAsync(row => row.Id == created.Id);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(created.Token[37..]))), stored.SecretHash);
                Assert.Equal(await App.AdminUserIdAsync(), stored.UserId);
            }
            using var revoke = await bff.DeleteAsync($"/api/v1/admin/credentials/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            using var revoked = await pat.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
    }

    [PostgresFact]
    public async Task Pat_works_without_a_bff_secret_but_cannot_enter_bff_presentation_or_account_management()
    {
        using var actor = await App.AdministratorAsync();
        var post = NewContent("Protected", status: ContentStatus.Published);
        post.IsProtected = true;
        post.NewProtectPassword = IsolatedPostgres.NewPassword();
        post = await actor.Content.SaveAsync(post);
        var created = await actor.Credentials.CreateAsync(new CredentialCreateInput
        {
            Name = "External", Scope = ApiCredentialScope.Write, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(); // 沒有 BFF service token 仍須接受合法 PAT。
        using var client = api.CreateCredentialClient(created.Token, SiteDefaults.DefaultSiteId);
        // 即使偽造 bootstrap 轉發標頭，也不可覆寫 PAT 的 scope 或提升為 BFF。
        client.DefaultRequestHeaders.Add(AccessClaims.UserHeader, await App.AdminUserIdAsync());
        client.DefaultRequestHeaders.Add(AccessClaims.StampHeader, await App.AdminSecurityStampAsync());
        using var allowed = await client.GetAsync("/api/v1/admin/contents?kind=Post");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        foreach (var path in new[]
        {
            "/api/v1/admin/access", "/api/v1/admin/sites", "/api/v1/admin/users", "/api/v1/admin/members",
            "/api/v1/admin/credentials", $"/api/v1/admin/contents/published/Post/{post.Slug}",
            "/api/v1/admin/contents/published-page/unknown", $"/api/v1/admin/contents/{post.Id}/unlock-state"
        })
        {
            using var denied = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        foreach (var (method, path, body) in new (HttpMethod, string, object)[]
        {
            (HttpMethod.Post, "/api/v1/admin/sites", new SiteCreateInput()),
            (HttpMethod.Post, "/api/v1/admin/users", new UserCreateInput()),
            (HttpMethod.Put, "/api/v1/admin/members", new MemberEditInput()),
            (HttpMethod.Post, "/api/v1/admin/credentials", new CredentialCreateInput()),
            (HttpMethod.Delete, $"/api/v1/admin/credentials/{created.Id}", new { }),
            (HttpMethod.Post, $"/api/v1/admin/contents/{post.Id}/unlock", new { password = post.NewProtectPassword }),
            (HttpMethod.Post, $"/api/v1/admin/contents/{post.Id}/view", new { })
        })
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
            using var denied = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var publicContent = await client.GetAsync($"/api/v1/posts/{post.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, publicContent.StatusCode);
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.ContentViews.ToListAsync());
        Assert.Null((await db.ApiCredentials.SingleAsync()).RevokedAt);
        Assert.Single(await db.Users.ToListAsync());
    }

    [PostgresFact]
    public async Task Existing_bff_and_pat_clients_observe_demotion_removal_and_account_revocation_immediately()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var user = await CreateMemberAsync(SiteRole.Editor);
        using var owner = await App.UserAsync(user.Id);
        var current = (await owner.Users.FindByIdAsync(user.Id))!;
        var credential = await owner.Credentials.CreateAsync(new CredentialCreateInput
        {
            Name = "Member", Scope = ApiCredentialScope.Write, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: token);
        using var bff = api.CreateBffClient(token, user.Id, current.SecurityStamp!);
        using var pat = api.CreateCredentialClient(credential.Token, SiteDefaults.DefaultSiteId);
        foreach (var client in new[] { bff, pat })
        {
            using var response = await client.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var admin = await App.AdministratorAsync();
        var member = (await admin.Sites.ListMembersAsync()).Single(row => row.UserId == user.Id);
        await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = user.UserName, Role = SiteRole.Reader, Version = member.Version });
        foreach (var client in new[] { bff, pat })
        {
            using var read = await client.GetAsync("/api/v1/admin/contents?kind=Post");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var write = await client.PostAsJsonAsync("/api/v1/admin/contents", NewContent("Denied after demotion"), Json);
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        }
        member = (await admin.Sites.ListMembersAsync()).Single(row => row.UserId == user.Id);
        await admin.Sites.RemoveMemberAsync(user.Id, member.Version);
        using var removedBff = await bff.GetAsync("/api/v1/admin/contents?kind=Post");
        Assert.Equal(HttpStatusCode.Forbidden, removedBff.StatusCode);
        using var removedPat = await pat.GetAsync("/api/v1/admin/contents?kind=Post");
        Assert.Equal(HttpStatusCode.Unauthorized, removedPat.StatusCode);
        await admin.Sites.SetUserDisabledAsync(user.Id, true);
        using var disabledBff = await bff.GetAsync("/api/v1/admin/contents?kind=Post");
        Assert.Equal(HttpStatusCode.Unauthorized, disabledBff.StatusCode);
        await admin.Sites.SetUserDisabledAsync(user.Id, false);
        using var staleBff = await bff.GetAsync("/api/v1/admin/contents?kind=Post");
        Assert.Equal(HttpStatusCode.Unauthorized, staleBff.StatusCode);
        await using var db = await App.CreateContextAsync();
        Assert.Empty(await db.Contents.ToListAsync());
    }

    [PostgresFact]
    public async Task Site_header_is_explicit_and_host_resolution_is_exact_with_no_disabled_fallback()
    {
        var site = await CreateSiteAsync();
        using var first = await App.AdministratorAsync();
        using var second = await App.AdministratorAsync(site.Id);
        var a = NewContent("Default body", status: ContentStatus.Published);
        a.Slug = "shared";
        var b = NewContent("Second body", status: ContentStatus.Published);
        b.Slug = "shared";
        await first.Content.SaveAsync(a);
        await second.Content.SaveAsync(b);
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync();
        using var defaults = api.CreateClient();
        using var selected = api.CreateClient(site.Id);
        using var defaultPost = await defaults.GetAsync("/api/v1/posts/shared");
        using var selectedPost = await selected.GetAsync("/api/v1/posts/shared");
        Assert.Equal(HttpStatusCode.OK, defaultPost.StatusCode);
        Assert.Equal(HttpStatusCode.OK, selectedPost.StatusCode);
        Assert.Equal("Default body", JsonNode.Parse(await defaultPost.Content.ReadAsStringAsync())!["title"]!.GetValue<string>());
        Assert.Equal("Second body", JsonNode.Parse(await selectedPost.Content.ReadAsStringAsync())!["title"]!.GetValue<string>());
        using (var malformed = api.CreateClient())
        {
            malformed.DefaultRequestHeaders.Add(AccessClaims.SiteHeader, "not-a-guid");
            using var response = await malformed.GetAsync("/api/v1/posts");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var duplicate = api.CreateClient(site.Id))
        {
            duplicate.DefaultRequestHeaders.Add(AccessClaims.SiteHeader, SiteDefaults.DefaultSiteId.ToString("D"));
            using var response = await duplicate.GetAsync("/api/v1/posts");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var exact = await defaults.GetAsync("/api/v1/sites/resolve?host=SECOND.EXAMPLE.TEST.");
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        Assert.Equal(site.Id, (await exact.Content.ReadFromJsonAsync<SiteSummary>(Json))!.Id);
        // 未登錄網域不得解析到任何站點；只有 loopback 在預設站未綁網域時退回預設站。
        using var legacy = await defaults.GetAsync("/api/v1/sites/resolve?host=legacy.example.test");
        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
        using var loopback = await defaults.GetAsync("/api/v1/sites/resolve?host=localhost");
        Assert.Equal(SiteDefaults.DefaultSiteId, (await loopback.Content.ReadFromJsonAsync<SiteSummary>(Json))!.Id);
        await first.Sites.UpdateSiteAsync(site.Id, new SiteUpdateInput
        {
            Name = site.Name, HostName = site.HostName, IsActive = false, Version = site.Version
        });
        using var disabledHost = await defaults.GetAsync("/api/v1/sites/resolve?host=second.example.test");
        using var disabledSite = await selected.GetAsync("/api/v1/posts/shared");
        Assert.Equal(HttpStatusCode.NotFound, disabledHost.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, disabledSite.StatusCode);
        var defaultSite = (await first.Sites.ListSitesAsync()).Single(row => row.Id == SiteDefaults.DefaultSiteId);
        await first.Sites.UpdateSiteAsync(defaultSite.Id, new SiteUpdateInput
        {
            Name = defaultSite.Name, HostName = "default.example.test", Version = defaultSite.Version
        });
        using var unknown = await defaults.GetAsync("/api/v1/sites/resolve?host=evil.default.example.test");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        // API 不使用請求 Host 或 X-Forwarded-Host 代替明確站點標頭。
        defaults.DefaultRequestHeaders.Host = site.HostName;
        defaults.DefaultRequestHeaders.Add("X-Forwarded-Host", site.HostName);
        using var stillDefault = await defaults.GetAsync("/api/v1/posts/shared");
        Assert.Equal(HttpStatusCode.OK, stillDefault.StatusCode);
        Assert.Equal("Default body", JsonNode.Parse(await stillDefault.Content.ReadAsStringAsync())!["title"]!.GetValue<string>());
    }

    [PostgresFact]
    public async Task Bff_manages_sites_users_and_members_end_to_end()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: token);
        using var bff = api.CreateBffClient(token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync());

        // 建站：回傳 201，建立者自動成為新站的站點管理員。
        using var created = await bff.PostAsJsonAsync("/api/v1/admin/sites", new SiteCreateInput
        {
            Name = "第三站", Slug = "third", HostName = "third.example.test"
        }, Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var site = await created.Content.ReadFromJsonAsync<SiteSummary>(Json);
        Assert.NotNull(site);
        Assert.Equal("third.example.test", site.HostName);

        // 改站名（帶並發版本）。
        using var updated = await bff.PutAsJsonAsync($"/api/v1/admin/sites/{site.Id}", new SiteUpdateInput
        {
            Name = "第三站改名", HostName = site.HostName, IsActive = true, Version = site.Version
        }, Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        // 建立使用者、停用、再啟用（戳記輪替由服務層負責）。
        using var userResponse = await bff.PostAsJsonAsync("/api/v1/admin/users", new UserCreateInput
        {
            UserName = "bff-user", Password = IsolatedPostgres.NewPassword()
        }, Json);
        Assert.Equal(HttpStatusCode.Created, userResponse.StatusCode);
        var user = await userResponse.Content.ReadFromJsonAsync<UserSummary>(Json);
        Assert.NotNull(user);
        using (var disabled = await bff.PutAsJsonAsync($"/api/v1/admin/users/{user.Id}/state", new UserStateInput(true), Json))
            Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        using (var enabled = await bff.PutAsJsonAsync($"/api/v1/admin/users/{user.Id}/state", new UserStateInput(false), Json))
            Assert.Equal(HttpStatusCode.NoContent, enabled.StatusCode);

        // 加入成員（預設站）、列出、移除。
        using (var added = await bff.PutAsJsonAsync("/api/v1/admin/members",
            new MemberEditInput { UserName = "bff-user", Role = SiteRole.Editor }, Json))
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        using var listResponse = await bff.GetAsync("/api/v1/admin/members");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var members = await listResponse.Content.ReadFromJsonAsync<List<MemberSummary>>(Json);
        var member = Assert.Single(members!, row => row.UserId == user.Id);
        Assert.Equal(SiteRole.Editor, member.Role);
        using (var removed = await bff.DeleteAsync($"/api/v1/admin/members/{user.Id}?version={member.Version}"))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        await using var db = await App.CreateContextAsync();
        Assert.Equal(2, await db.Sites.CountAsync());
        Assert.Equal("第三站改名", (await db.Sites.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(row => row.Id == site.Id)).Name);
        // 建立者是新站的站點管理員；bff-user 的預設站成員資格已移除。
        Assert.Single(await db.SiteMemberships.IgnoreQueryFilters()
            .Where(row => row.SiteId == site.Id && row.Role == SiteRole.Administrator).ToListAsync());
        Assert.Empty(await db.SiteMemberships.IgnoreQueryFilters()
            .Where(row => row.UserId == user.Id).ToListAsync());
    }

    [PostgresFact]
    public async Task Pat_traffic_is_limited_per_credential_while_the_bff_stays_exempt()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var owner = await App.AdministratorAsync();
        CredentialCreateInput Input(string name) => new()
        {
            Name = name, Scope = ApiCredentialScope.Read, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        var first = await owner.Credentials.CreateAsync(Input("limited"));
        var second = await owner.Credentials.CreateAsync(Input("spare"));
        await using var api = new RunningApi(App.ConnectionString);
        await api.StartAsync(serviceToken: token);
        using var bff = api.CreateBffClient(token, await App.AdminUserIdAsync(), await App.AdminSecurityStampAsync());
        using var pat = api.CreateCredentialClient(first.Token, SiteDefaults.DefaultSiteId);

        // BFF 服務通行證不受限流：超過 PAT 上限的連續呼叫全部成功。
        for (var attempt = 0; attempt < 130; attempt++)
        {
            using var ok = await bff.GetAsync("/api/v1/admin/access");
            Assert.True(ok.StatusCode == HttpStatusCode.OK,
                $"BFF 不應被限流（第 {attempt + 1} 次得到 {ok.StatusCode}）。");
        }

        // PAT 以憑證為單位限流（120/分）；限額用盡後收到 429。
        var accepted = 0;
        var rejected = 0;
        for (var attempt = 0; attempt < 130; attempt++)
        {
            using var response = await pat.GetAsync("/api/v1/admin/contents?kind=Post");
            if (response.StatusCode == HttpStatusCode.OK) accepted++;
            else if (response.StatusCode == HttpStatusCode.TooManyRequests) rejected++;
            else Assert.Fail("PAT 收到非預期狀態碼：" + response.StatusCode);
        }
        Assert.True(rejected > 0, "PAT 必須在限額用盡後收到 429。");
        Assert.True(accepted <= 120, $"PAT 限額應為 120/分，實際通過 {accepted} 次。");

        // 限流以憑證為分區：另一張憑證不受第一張超額的影響。
        using var otherPat = api.CreateCredentialClient(second.Token, SiteDefaults.DefaultSiteId);
        using var other = await otherPat.GetAsync("/api/v1/admin/contents?kind=Post");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
}
