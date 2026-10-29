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
