namespace Origami.Core.Services;

public static class PublicHttpUrlPolicy
{
    public static bool IsAllowed(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 &&
        string.IsNullOrEmpty(uri.UserInfo) && IsAllowedHostName(uri.DnsSafeHost) &&
        (!IPAddress.TryParse(uri.Host, out var address) || IsPublicAddress(address));

    public static bool IsAllowedHostName(string host)
    {
        host = host.TrimEnd('.');
        return host.Length > 0 && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 => bytes[1] is < 64 or > 127,
                169 => bytes[1] != 254,
                172 => bytes[1] is < 16 or > 31,
                192 => bytes[1] switch
                {
                    168 => false,
                    0 => false,
                    _ => true
                },
                198 => bytes[1] is not (18 or 19 or 51),
                203 => bytes[1] != 0 || bytes[2] != 113,
                >= 224 => false,
                _ => true
            };
        }
        if (IsZeroPrefix(bytes, 12) || IsNat64Prefix(bytes) || IsTranslatedPrefix(bytes))
            return IsPublicAddress(new IPAddress(bytes.AsSpan(12, 4)));
        // 6to4 可在中繼站解碼出內網 IPv4；此舊機制不再允許作為出站目標。
        if (bytes[0] == 0x20 && bytes[1] == 0x02) return false;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
            (bytes[0] & 0xfe) == 0xfc || (bytes[0] & 0xe0) != 0x20)
        {
            return false;
        }
        return !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] is 0x00 or 0x02) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x01 && bytes[3] is 0x10 or 0x20) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }

    private static bool IsZeroPrefix(byte[] bytes, int length)
    {
        for (var i = 0; i < length; i++)
            if (bytes[i] != 0) return false;
        return true;
    }

    private static bool IsNat64Prefix(byte[] bytes) =>
        bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b &&
        IsZeroPrefix(bytes[4..], 8);

    private static bool IsTranslatedPrefix(byte[] bytes) =>
        IsZeroPrefix(bytes, 10) && bytes[10] == 0xff && bytes[11] == 0xff;
}

public sealed class PublicNetworkHttpHandler : DelegatingHandler
{
    public PublicNetworkHttpHandler(Func<string, CancellationToken, ValueTask<IPAddress[]>>? resolver = null)
        : base(CreateInnerHandler(resolver))
    {
    }

    private static SocketsHttpHandler CreateInnerHandler(Func<string, CancellationToken, ValueTask<IPAddress[]>>? resolver)
    {
        var resolve = resolver ?? ((host, ct) => new ValueTask<IPAddress[]>(Dns.GetHostAddressesAsync(host, ct)));
        // 代理會使連線端點變成代理本身，無法再驗證最終目標，因此出站一律直連。
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = (context, ct) => ConnectAsync(context, resolve, ct)
        };
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        Func<string, CancellationToken, ValueTask<IPAddress[]>> resolver,
        CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        if (!PublicHttpUrlPolicy.IsAllowedHostName(endpoint.Host))
            throw new HttpRequestException("目標網址不是公開 HTTPS 位址。");
        IPAddress[] addresses;
        if (IPAddress.TryParse(endpoint.Host, out var literal))
        {
            addresses = PublicHttpUrlPolicy.IsPublicAddress(literal) ? [literal] : [];
        }
        else
        {
            addresses = await resolver(endpoint.Host, ct);
        }
        if (addresses.Length == 0 || addresses.Any(x => !PublicHttpUrlPolicy.IsPublicAddress(x)))
            throw new HttpRequestException("目標網址解析到不公開的位址。");

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, endpoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                socket.Dispose();
                lastError = error;
            }
        }
        throw new HttpRequestException("無法連線到目標公開位址。", lastError);
    }
}
