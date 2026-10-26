namespace Origami.Web.Services;

/// <summary>
/// RFC 6238 TOTP (SHA-1, 30 s step, 6 digits) with a one-step drift window on each side.
/// Implemented locally so two-factor login adds no new dependency.
/// </summary>
public static class Totp
{
    private const int StepSeconds = 30;
    private const int Digits = 6;

    /// <summary>Generates a new 20-byte shared secret encoded as Base32.</summary>
    public static string GenerateSecret() =>
        Base32Encode(RandomNumberGenerator.GetBytes(20));

    /// <summary>Constant-time verification accepting the previous, current and next step.</summary>
    public static bool Verify(string base32Secret, string? code, TimeProvider clock)
    {
        if (string.IsNullOrEmpty(code) || code.Length != Digits || !code.All(char.IsDigit)) return false;
        var key = Base32Decode(base32Secret);
        if (key.Length == 0) return false;
        var step = clock.GetUtcNow().ToUnixTimeSeconds();
        for (var drift = -1; drift <= 1; drift++)
        {
            var candidate = Compute(key, (step + drift * StepSeconds) / StepSeconds);
            if (CryptographicOperations.FixedTimeEquals(
                    BitConverter.GetBytes(candidate), BitConverter.GetBytes(int.Parse(code, CultureInfo.InvariantCulture))))
                return true;
        }
        return false;
    }

    public static string OtpAuthUri(string base32Secret, string account, string issuer) =>
        "otpauth://totp/" + Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account)
        + "?secret=" + base32Secret + "&issuer=" + Uri.EscapeDataString(issuer) + "&algorithm=SHA1&digits=6&period=30";

    private static int Compute(byte[] key, long step)
    {
        Span<byte> message = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            message[i] = (byte)(step & 0xff);
            step >>= 8;
        }
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(message.ToArray());
        var offset = hash[^1] & 0x0f;
        return ((hash[offset] << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3]) & 0x7fffffff) % 1_000_000;
    }

    private static string Base32Encode(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) result.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        var bits = 0;
        var buffer = 0;
        var bytes = new List<byte>();
        foreach (var character in value)
        {
            var index = character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= 'a' and <= 'z' => character - 'a',
                >= '2' and <= '7' => character - '2' + 26,
                _ => -1
            };
            if (index < 0) continue;
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8) continue;
            bytes.Add((byte)((buffer >> (bits - 8)) & 0xff));
            bits -= 8;
        }
        return [.. bytes];
    }
}
