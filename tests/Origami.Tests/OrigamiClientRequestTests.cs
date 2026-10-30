namespace Origami.Tests;

public sealed class OrigamiClientRequestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editing_heartbeat_uses_post(bool takeOver)
    {
        var id = Guid.NewGuid();
        using var handler = new RecordingHandler(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ContentService.EditingLockState("user", "Writer", null, false))
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var api = Client(http);
        var state = await api.TouchEditingAsync(id, takeOver);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/v1/admin/contents/{id}/editing" + (takeOver ? "?takeOver=true" : ""), handler.Path);
        Assert.Equal("Writer", state?.DisplayName);
    }

    [Fact]
    public async Task Comment_writes_accept_empty_success_responses()
    {
        using var handler = new RecordingHandler(_ => new(HttpStatusCode.NoContent));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var api = Client(http);
        var id = Guid.NewGuid();
        var version = Guid.NewGuid();
        await api.PostCommentAsync(id, "Visitor", null, "Hello", false);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/v1/admin/contents/{id}/comments", handler.Path);
        await api.ModerateCommentAsync(id, CommentStatus.Approved, version);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/v1/admin/comments/{id}/moderate?version={version}", handler.Path);
        await api.DeleteCommentAsync(id, version);
        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal($"/api/v1/admin/comments/{id}?version={version}", handler.Path);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Editing_release_preserves_authentication_and_permission_errors(HttpStatusCode status)
    {
        using var handler = new RecordingHandler(_ => new(status));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var api = Client(http);
        if (status == HttpStatusCode.Unauthorized)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => api.ReleaseEditingAsync(Guid.NewGuid()));
        else
            await Assert.ThrowsAsync<AccessDeniedException>(() => api.ReleaseEditingAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Every_request_replaces_default_identity_headers_and_expired_snapshot_becomes_anonymous()
    {
        var deadline = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var clock = new RequestClock(deadline.AddHours(-1));
        var auth = new MutableAuthenticationStateProvider();
        auth.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "request-user"),
            new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, "request-stamp"),
            LoginSessionLifetime.CreateClaim(deadline)
        ], "test")));
        var observed = new List<string[]>();
        using var handler = new RecordingHandler(request =>
        {
            observed.Add(new[] { AccessClaims.UserHeader, AccessClaims.StampHeader, AccessClaims.SessionExpiresHeader }
                .Select(name => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "").ToArray());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<PageNavItem>()) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        http.DefaultRequestHeaders.Add(AccessClaims.UserHeader, "old-user");
        http.DefaultRequestHeaders.Add(AccessClaims.StampHeader, "old-stamp");
        http.DefaultRequestHeaders.Add(AccessClaims.SessionExpiresHeader, "253402300799");
        var api = new OrigamiClient(http,
            new AdminSession(new HttpContextAccessor(), auth, Options.Create(new IdentityOptions()), clock));
        await api.GetNavigationAsync();
        clock.UtcNow = deadline.AddTicks(-1);
        await api.GetNavigationAsync();
        clock.UtcNow = deadline;
        await api.GetNavigationAsync();
        Assert.Equal(new[] { "request-user", "request-stamp", deadline.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) }, observed[0]);
        Assert.Equal(observed[0], observed[1]);
        Assert.Equal(new[] { "", "", "" }, observed[2]);
        Assert.Equal("old-user", Assert.Single(http.DefaultRequestHeaders.GetValues(AccessClaims.UserHeader)));
    }

    private sealed class RequestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static OrigamiClient Client(HttpClient http) => new(http,
        new AdminSession(new HttpContextAccessor(), new AnonymousAuthentication(), Options.Create(new IdentityOptions())));

    private sealed class AnonymousAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.PathAndQuery;
            return Task.FromResult(response(request));
        }
    }
}
