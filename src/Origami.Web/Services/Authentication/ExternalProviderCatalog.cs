namespace Origami.Web.Services;

public sealed record ExternalProviderDescriptor(string Name, string DisplayName);

public sealed class ExternalProviderCatalog
{
    private readonly ExternalProviderConfiguration[] providers;

    public ExternalProviderCatalog(IConfiguration configuration)
    {
        providers = new[] { "Google", "Microsoft", "GitHub", "WeChat" }
            .Select(name => ReadProvider(configuration.GetSection("Authentication:External:" + name), name))
            .OfType<ExternalProviderConfiguration>().ToArray();
    }

    public IReadOnlyList<ExternalProviderDescriptor> ForOrigin(string origin)
    {
        var normalized = NormalizeOrigin(origin);
        return normalized is null ? [] : providers.Where(provider => provider.Origins.Contains(normalized))
            .Select(provider => new ExternalProviderDescriptor(provider.Name, provider.Name)).ToArray();
    }

    public bool IsAllowed(string provider, string origin)
    {
        var normalized = NormalizeOrigin(origin);
        return normalized is not null && providers.Any(candidate =>
            candidate.Name == provider && candidate.Origins.Contains(normalized));
    }

    internal IReadOnlyList<ExternalProviderConfiguration> Providers => providers;

    internal static string? NormalizeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > 2048 || origin.Any(char.IsWhiteSpace) ||
            origin.Any(char.IsControl) || !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            origin.Contains('\\') || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) || uri.Host.Contains('*') ||
            uri.HostNameType is UriHostNameType.Unknown or UriHostNameType.Basic || uri.Port <= 0 ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return null;

        // 不接受被 URI 正規化消除的路徑；白名單只能登錄 origin，不能登錄 URL 前綴。
        var authority = origin[(origin.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var slash = authority.IndexOf('/');
        if (slash >= 0 && slash != authority.Length - 1) return null;
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    private static ExternalProviderConfiguration? ReadProvider(IConfigurationSection section, string name)
    {
        if (!bool.TryParse(section["Enabled"], out var enabled) || !enabled) return null;
        var clientId = section[name == "WeChat" ? "AppId" : "ClientId"];
        var clientSecret = section[name == "WeChat" ? "AppSecret" : "ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 256 || clientId.Any(char.IsWhiteSpace) ||
            clientId.Any(char.IsControl) || string.IsNullOrWhiteSpace(clientSecret) || clientSecret.Length > 4096 ||
            clientSecret.Any(char.IsControl)) return null;

        var originsSection = section.GetSection("AllowedOrigins");
        if (originsSection.Value is not null) return null;
        var entries = originsSection.GetChildren().ToArray();
        if (entries.Length is 0 or > 64) return null;
        var origins = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Key != i.ToString(CultureInfo.InvariantCulture) || entries[i].GetChildren().Any()) return null;
            var origin = NormalizeOrigin(entries[i].Value);
            if (origin is null) return null;
            origins.Add(origin);
        }
        return new ExternalProviderConfiguration(name, clientId, clientSecret, origins);
    }
}

// 不使用 record，避免自動產生的 ToString 將秘密帶入診斷輸出。
internal sealed class ExternalProviderConfiguration(string name, string clientId, string clientSecret, HashSet<string> origins)
{
    internal string Name { get; } = name;
    internal string ClientId { get; } = clientId;
    internal string ClientSecret { get; } = clientSecret;
    internal HashSet<string> Origins { get; } = origins;
}
