namespace Origami.Web.Services;

internal sealed class WeChatOAuthHandler(IOptionsMonitor<WeChatOAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : ExternalOAuthHandler<WeChatOAuthOptions>(options, logger, encoder)
{
    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri) =>
        QueryHelpers.AddQueryString(Options.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["appid"] = Options.AppId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "snsapi_login",
            ["state"] = Options.StateDataFormat.Protect(properties)
        }) + "#wechat_redirect";

    protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
    {
        var endpoint = QueryHelpers.AddQueryString(Options.TokenEndpoint, new Dictionary<string, string?>
        {
            ["appid"] = Options.AppId,
            ["secret"] = Options.AppSecret,
            ["code"] = context.Code,
            ["grant_type"] = "authorization_code"
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await Backchannel.SendAsync(request, Context.RequestAborted);
        if (!response.IsSuccessStatusCode) return FailedResponse();

        // 微信可能用 text/plain 傳 JSON，且用 HTTP 200 表示協定失敗；不信任狀態碼或 MIME 單獨判定成功。
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));
        var root = payload.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            (root.TryGetProperty("errcode", out var error) &&
                (error.ValueKind != JsonValueKind.Number || !error.TryGetInt32(out var errorCode) || errorCode != 0)) ||
            !root.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(token.GetString()) || !TryGetOpenId(root, out _))
        {
            payload.Dispose();
            return FailedResponse();
        }
        return OAuthTokenResponse.Success(payload);
    }

    protected override async Task<AuthenticationTicket> CreateTicketAsync(ClaimsIdentity identity,
        AuthenticationProperties properties, OAuthTokenResponse tokens)
    {
        if (tokens.Response is null || !TryGetOpenId(tokens.Response.RootElement, out var openId))
            throw new AuthenticationFailureException(ExternalOAuthSecurity.FailureMessage);

        // openid 只在 AppId 內穩定；直接使用驗證換碼結果，不再抓個資或以 unionid／Email 合併帳號。
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Options.AppId + ":" + openId,
            ClaimValueTypes.String, Scheme.Name));
        var context = new OAuthCreatingTicketContext(new ClaimsPrincipal(identity), properties, Context,
            Scheme, Options, Backchannel, tokens, tokens.Response.RootElement);
        await Events.CreatingTicket(context);
        return new AuthenticationTicket(context.Principal!, context.Properties, Scheme.Name);
    }

    private static bool TryGetOpenId(JsonElement root, out string? openId)
    {
        openId = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("openid", out var value) ||
            value.ValueKind != JsonValueKind.String) return false;
        openId = value.GetString();
        return openId is { Length: > 0 and <= 128 } &&
            openId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    private static OAuthTokenResponse FailedResponse() =>
        OAuthTokenResponse.Failed(new AuthenticationFailureException(ExternalOAuthSecurity.FailureMessage));
}
