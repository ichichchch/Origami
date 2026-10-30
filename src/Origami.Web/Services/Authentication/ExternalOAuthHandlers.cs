namespace Origami.Web.Services;

internal sealed class ExternalGoogleHandler(IOptionsMonitor<GoogleOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : GoogleHandler(options, new ExternalAuthenticationLoggerFactory(logger), encoder)
{
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ExternalOAuthSecurity.ChallengeAsync(Context, Scheme.Name, Options, properties, base.HandleChallengeAsync);

    protected override Task<HandleRequestResult> HandleRemoteAuthenticateAsync() =>
        ExternalOAuthSecurity.AuthenticateAsync(Context, Scheme.Name, Options, base.HandleRemoteAuthenticateAsync);
}

internal sealed class ExternalMicrosoftHandler(IOptionsMonitor<MicrosoftAccountOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : MicrosoftAccountHandler(options, new ExternalAuthenticationLoggerFactory(logger), encoder)
{
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ExternalOAuthSecurity.ChallengeAsync(Context, Scheme.Name, Options, properties, base.HandleChallengeAsync);

    protected override Task<HandleRequestResult> HandleRemoteAuthenticateAsync() =>
        ExternalOAuthSecurity.AuthenticateAsync(Context, Scheme.Name, Options, base.HandleRemoteAuthenticateAsync);
}

internal class ExternalOAuthHandler<TOptions>(IOptionsMonitor<TOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : OAuthHandler<TOptions>(options, new ExternalAuthenticationLoggerFactory(logger), encoder) where TOptions : OAuthOptions, new()
{
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ExternalOAuthSecurity.ChallengeAsync(Context, Scheme.Name, Options, properties, base.HandleChallengeAsync);

    protected override Task<HandleRequestResult> HandleRemoteAuthenticateAsync() =>
        ExternalOAuthSecurity.AuthenticateAsync(Context, Scheme.Name, Options, base.HandleRemoteAuthenticateAsync);
}
