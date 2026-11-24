namespace Origami.Web.Services;

public sealed class CliSession
{
    private ClaimsPrincipal principal = new(new ClaimsIdentity());
    public bool IsAuthenticated => principal.Identity?.IsAuthenticated == true;
    public ClaimsPrincipal Principal => principal;

    public void SignIn(AdminUser user, IdentityOptions options, TimeProvider clock)
    {
        var identity = new ClaimsIdentity("Origami.Cli");
        identity.AddClaim(new Claim(options.ClaimsIdentity.UserIdClaimType, user.Id));
        identity.AddClaim(new Claim(options.ClaimsIdentity.UserNameClaimType, user.UserName ?? ""));
        identity.AddClaim(new Claim(options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp ?? ""));
        identity.AddClaim(LoginSessionLifetime.CreateClaim(clock.GetUtcNow().AddMinutes(15)));
        principal = new ClaimsPrincipal(identity);
    }
}

public sealed class CliAuthenticationStateProvider(CliSession session) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(session.Principal));
}
