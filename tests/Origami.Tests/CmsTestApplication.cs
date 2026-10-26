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
    public string ConnectionString { get; }
    public ServiceProvider Services { get; }
    public AdminProvisioner Provisioner => Services.GetRequiredService<AdminProvisioner>();

    private CmsTestApplication(IsolatedPostgres postgres, string connectionString)
    {
        this.postgres = postgres;
        ConnectionString = connectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<CmsDbContext>(options => options.UseNpgsql(connectionString, o => o.UseVector()));
        services.AddCmsIdentity();
        // Password-reset tokens stay within this test application, without reading/writing host keys.
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddScoped<MutableAuthenticationStateProvider>();
        services.Replace(ServiceDescriptor.Scoped<AuthenticationStateProvider>(provider =>
            provider.GetRequiredService<MutableAuthenticationStateProvider>()));
        services.AddSingleton(TimeProvider.System);
        // A singleton fake keeps per-test configuration visible to the worker's own scopes.
        services.AddSingleton<FakeEmbeddingGateway>();
        services.AddSingleton<IEmbeddingGateway>(provider => provider.GetRequiredService<FakeEmbeddingGateway>());
        services.AddScoped<SemanticSearchService>();
        services.AddSingleton<MarkdownService>();
        services.AddScoped<PublicSearchService>();
        // Deliberately not bound to host configuration or inherited AI credentials.
        services.AddOptions<AiOptions>();
        services.AddScoped<AiStatusService>();
        services.AddScoped<AdminAccess>();
        services.AddScoped<ContentService>();
        services.AddScoped<ExportService>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<TaxonomyService>();
        services.AddScoped<SettingsService>();
        // The assistant runs against the shared scripted client; no provider or key is involved.
        services.AddSingleton<FakeChatClient>();
        services.AddSingleton<IAssistantChatClientFactory>(provider =>
            new StubAssistantChatClientFactory(provider.GetRequiredService<FakeChatClient>()));
        services.AddScoped<McpReadTools>();
        services.AddScoped<SiteAssistant>();
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
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

    public TestActor Anonymous() => new(Services.CreateScope());
    public FakeEmbeddingGateway Embeddings => Services.GetRequiredService<FakeEmbeddingGateway>();
    public FakeChatClient Chat => Services.GetRequiredService<FakeChatClient>();
    public EmbeddingJobWorker Worker => new(Services.GetRequiredService<IDbContextFactory<CmsDbContext>>(), Services,
        Services.GetRequiredService<TimeProvider>(), NullLogger<EmbeddingJobWorker>.Instance);

    public async Task<TestActor> AdministratorAsync()
    {
        var actor = Anonymous();
        try
        {
            var user = await actor.Users.Users.SingleAsync();
            var factory = actor.Services.GetRequiredService<IUserClaimsPrincipalFactory<AdminUser>>();
            actor.Authentication.SetPrincipal(await factory.CreateAsync(user));
            return actor;
        }
        catch { actor.Dispose(); throw; }
    }

    public Task<CmsDbContext> CreateContextAsync() =>
        Services.GetRequiredService<IDbContextFactory<CmsDbContext>>().CreateDbContextAsync();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await postgres.DropDatabaseAsync(ConnectionString);
    }
}

public sealed class TestActor(IServiceScope scope) : IDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;
    public UserManager<AdminUser> Users => Services.GetRequiredService<UserManager<AdminUser>>();
    public MutableAuthenticationStateProvider Authentication => Services.GetRequiredService<MutableAuthenticationStateProvider>();
    public ContentService Content => Services.GetRequiredService<ContentService>();
    public TaxonomyService Taxonomy => Services.GetRequiredService<TaxonomyService>();
    public SettingsService Settings => Services.GetRequiredService<SettingsService>();
    public SemanticSearchService Semantic => Services.GetRequiredService<SemanticSearchService>();
    public SiteAssistant Assistant => Services.GetRequiredService<SiteAssistant>();
    public void Dispose() => scope.Dispose();
}

public abstract class ServiceTestBase(IsolatedPostgres postgres) : IAsyncLifetime
{
    protected CmsTestApplication App { get; private set; } = null!;
    protected virtual bool InitializeAdministrator => true;

    public async Task InitializeAsync()
    {
        // Missing binaries are reported by the test attributes before any test executes.
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

    protected static ContentEdit NewContent(string title = "Test post", ContentKind kind = ContentKind.Post,
        ContentStatus status = ContentStatus.Draft) => new() { Title = title, Kind = kind, Status = status, Markdown = "Test body" };
}
