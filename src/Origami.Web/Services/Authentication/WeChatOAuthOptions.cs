namespace Origami.Web.Services;

public sealed class WeChatOAuthOptions : OAuthOptions
{
    public WeChatOAuthOptions()
    {
        CallbackPath = "/signin-wechat";
        AuthorizationEndpoint = "https://open.weixin.qq.com/connect/qrconnect";
        TokenEndpoint = "https://api.weixin.qq.com/sns/oauth2/access_token";
        UserInformationEndpoint = "https://api.weixin.qq.com/sns/userinfo";
        Scope.Add("snsapi_login");
        UsePkce = false;
    }

    public string AppId { get => ClientId; set => ClientId = value; }
    public string AppSecret { get => ClientSecret; set => ClientSecret = value; }
}
