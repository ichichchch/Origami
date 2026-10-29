namespace Origami.Tests;

public sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
{
    private ClaimsPrincipal principal = new(new ClaimsIdentity());
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    public void SetPrincipal(ClaimsPrincipal value)
    {
        principal = value;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}

public sealed class CmsTestApplication : IAsyncDisposable
{
    private readonly IsolatedPostgres postgres;
    // CreateContextAsync 回傳的 context 由呼叫端釋放；其 factory scope 保留到應用釋放，
    // 避免 context 還在使用時，scoped 相依物件已經被釋放。
    private readonly ConcurrentBag<AsyncServiceScope> contextScopes = new();
    private readonly AsyncServiceScope defaultScope;
    // 匯出／匯入服務需要儲存路徑；測試一律指向拋棄式暫存目錄，絕不觸及實際目錄。
    private readonly TemporaryMediaStorage storageDirectory = new();
    public string ConnectionString { get; }
    public ServiceProvider Services { get; }
    public AdminProvisioner Provisioner => Services.GetRequiredService<AdminProvisioner>();

    private CmsTestApplication(IsolatedPostgres postgres, string connectionString)
    {
        this.postgres = postgres;
        ConnectionString = connectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<SiteContext>();
        services.AddDbContextFactory<CmsDbContext>(options => options.UseNpgsql(connectionString, o => o.UseVector()),
            ServiceLifetime.Scoped);
        services.AddCmsIdentity();
        // 密碼重設 token 只留在本測試應用程式內，不讀寫主機金鑰。
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddScoped<MutableAuthenticationStateProvider>();
        services.Replace(ServiceDescriptor.Scoped<AuthenticationStateProvider>(provider =>
            provider.GetRequiredService<MutableAuthenticationStateProvider>()));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(provider => storageDirectory.Create(provider.GetRequiredService<SiteContext>()));
        services.AddScoped<SiteAdministrationService>();
        services.AddScoped<ApiCredentialService>();
        services.AddScoped<MediaService>();
        // 以單例假物件讓各測試的組態變更，對背景工作自建的 scope 也可見。
        services.AddSingleton<FakeEmbeddingGateway>();
        services.AddSingleton<IEmbeddingGateway>(provider => provider.GetRequiredService<FakeEmbeddingGateway>());
        services.AddScoped<SemanticSearchService>();
        services.AddSingleton<MarkdownService>();
        services.AddScoped<PublicSearchService>();
        // 刻意不綁定主機組態，也不繼承 AI 認證資訊。
        services.AddOptions<AiOptions>();
        services.AddScoped<AiStatusService>();
        services.AddScoped<AdminAccess>();
        services.AddScoped<ContentService>();
        services.AddScoped<ContentTypeService>();
        services.AddScoped<ExportService>();
        services.AddScoped<ImportService>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<TaxonomyService>();
        services.AddScoped<NavigationService>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<CommentService>();
        services.AddSingleton<TestEmailSender>();
        services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<TestEmailSender>());
        services.AddScoped<WxrImportService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<WebhookService>();
        services.AddScoped<SiteHealthService>();
        services.AddOptions<BackupOptions>();
        services.AddScoped<BackupService>();
        // 助理對共用的腳本化用戶端運作；完全不涉及供應商或金鑰。
        services.AddSingleton<FakeChatClient>();
        services.AddSingleton<IAssistantChatClientFactory>(provider =>
            new StubAssistantChatClientFactory(provider.GetRequiredService<FakeChatClient>()));
        services.AddScoped<McpReadTools>();
        services.AddScoped<SiteAssistant>();
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        defaultScope = Services.CreateAsyncScope();
    }

    public static async Task<CmsTestApplication> CreateAsync(IsolatedPostgres postgres)
    {
        var connection = await postgres.CreateDatabaseAsync();
        try { return new(postgres, connection); }
        catch
        {
            await postgres.DropDatabaseAsync(connection);
            throw;
        }
    }

    public TestActor Anonymous(Guid? siteId = null)
    {
        var actor = new TestActor(Services.CreateScope());
        actor.Site.SiteId = siteId ?? SiteDefaults.DefaultSiteId;
        return actor;
    }

    public StoragePaths Storage => defaultScope.ServiceProvider.GetRequiredService<StoragePaths>();
    public IDbContextFactory<CmsDbContext> ContextFactory =>
        defaultScope.ServiceProvider.GetRequiredService<IDbContextFactory<CmsDbContext>>();
    public FakeEmbeddingGateway Embeddings => Services.GetRequiredService<FakeEmbeddingGateway>();
    public FakeChatClient Chat => Services.GetRequiredService<FakeChatClient>();
    public EmbeddingJobWorker Worker => new(Services,
        Services.GetRequiredService<TimeProvider>(), NullLogger<EmbeddingJobWorker>.Instance);

    public async Task<TestActor> AdministratorAsync(Guid? siteId = null)
    {
        await using var db = await CreateContextAsync();
        var id = await db.Users.Where(user => user.SingletonKey == 1).Select(user => user.Id).SingleAsync();
        return await UserAsync(id, siteId);
    }

    public async Task<TestActor> UserAsync(string userId, Guid? siteId = null)
    {
        var actor = Anonymous(siteId);
        try
        {
            var user = await actor.Users.Users.SingleAsync(user => user.Id == userId);
            var factory = actor.Services.GetRequiredService<IUserClaimsPrincipalFactory<AdminUser>>();
            actor.Authentication.SetPrincipal(await factory.CreateAsync(user));
            return actor;
        }
        catch { actor.Dispose(); throw; }
    }

    public Task<CmsDbContext> CreateContextAsync(Guid? siteId = null)
    {
        if (siteId is null || siteId == SiteDefaults.DefaultSiteId)
            return ContextFactory.CreateDbContextAsync();
        var scope = Services.CreateAsyncScope();
        contextScopes.Add(scope);
        scope.ServiceProvider.GetRequiredService<SiteContext>().SiteId = siteId.Value;
        return scope.ServiceProvider.GetRequiredService<IDbContextFactory<CmsDbContext>>().CreateDbContextAsync();
    }

    public async Task<string> AdminUserIdAsync()
    {
        await using var db = await CreateContextAsync();
        return await db.Users.Where(user => user.SingletonKey == 1).Select(user => user.Id).SingleAsync();
    }

    /// <summary>BFF 管理呼叫必須同時轉發使用者 Id 與目前的安全戳記。</summary>
    public async Task<string> AdminSecurityStampAsync()
    {
        await using var db = await CreateContextAsync();
        var stamp = await db.Users.Where(user => user.SingletonKey == 1).Select(user => user.SecurityStamp).SingleAsync();
        return stamp ?? "";
    }

    public async ValueTask DisposeAsync()
    {
        while (contextScopes.TryTake(out var scope)) await scope.DisposeAsync();
        await defaultScope.DisposeAsync();
        await Services.DisposeAsync();
        await postgres.DropDatabaseAsync(ConnectionString);
        storageDirectory.Dispose();
    }
}

public sealed class TestActor(IServiceScope scope) : IDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;
    public SiteContext Site => Services.GetRequiredService<SiteContext>();
    public AdminAccess Access => Services.GetRequiredService<AdminAccess>();
    public SiteAdministrationService Sites => Services.GetRequiredService<SiteAdministrationService>();
    public ApiCredentialService Credentials => Services.GetRequiredService<ApiCredentialService>();
    public MediaService Media => Services.GetRequiredService<MediaService>();
    public StoragePaths Storage => Services.GetRequiredService<StoragePaths>();
    public UserManager<AdminUser> Users => Services.GetRequiredService<UserManager<AdminUser>>();
    public MutableAuthenticationStateProvider Authentication => Services.GetRequiredService<MutableAuthenticationStateProvider>();
    public ContentService Content => Services.GetRequiredService<ContentService>();
    public TaxonomyService Taxonomy => Services.GetRequiredService<TaxonomyService>();
    public SettingsService Settings => Services.GetRequiredService<SettingsService>();
    public SemanticSearchService Semantic => Services.GetRequiredService<SemanticSearchService>();
    public SiteAssistant Assistant => Services.GetRequiredService<SiteAssistant>();
    public void Dispose() => scope.Dispose();
}

public abstract class ServiceTestBase : IAsyncLifetime
{
    private readonly IsolatedPostgres postgres;
    protected ServiceTestBase(IsolatedPostgres postgres)
    {
        this.postgres = postgres;
        Postgres = postgres;
    }

    protected CmsTestApplication App { get; private set; } = null!;
    /// <summary>集合共享的資料庫程序；升級測試等需要「非應用程式資料庫」時使用。</summary>
    protected IsolatedPostgres Postgres { get; }
    protected virtual bool InitializeAdministrator => true;

    public async Task InitializeAsync()
    {
        // 缺少二進位檔時，會在任何測試執行前由測試屬性回報。
        if (PostgresBinaries.Find() is null) return;
        App = await CmsTestApplication.CreateAsync(postgres);
        try
        {
            if (InitializeAdministrator)
                await App.Provisioner.InitializeAsync("test-admin", IsolatedPostgres.NewPassword(), CancellationToken.None);
        }
        catch
        {
            await App.DisposeAsync();
            App = null!;
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (App is not null) await App.DisposeAsync();
    }

    protected async Task<SiteSummary> CreateSiteAsync(string slug = "second")
    {
        using var admin = await App.AdministratorAsync();
        return await admin.Sites.CreateSiteAsync(new SiteCreateInput
        {
            Name = slug, Slug = slug, HostName = slug + ".example.test"
        });
    }

    protected async Task<UserSummary> CreateMemberAsync(SiteRole role, Guid? siteId = null, string name = "member")
    {
        using var admin = await App.AdministratorAsync(siteId);
        var user = await admin.Sites.CreateUserAsync(new UserCreateInput
        {
            UserName = name, Password = IsolatedPostgres.NewPassword()
        });
        await admin.Sites.SetMemberAsync(new MemberEditInput { UserName = name, Role = role });
        return user;
    }

    protected static ContentEdit NewContent(string title = "Test post", ContentKind kind = ContentKind.Post,
        ContentStatus status = ContentStatus.Draft) => new() { Title = title, Kind = kind, Status = status, Markdown = "Test body" };
}
