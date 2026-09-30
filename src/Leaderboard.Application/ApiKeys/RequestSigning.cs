using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Leaderboard.Application.ApiKeys;

/// <summary>
/// HMAC-SHA256 request signing shared by the server and by clients (samples, tests, Swagger helper).
/// <code>
/// canonical = METHOD \n PATH \n TIMESTAMP \n NONCE \n hex(SHA256(body))
/// signature = Base64(HMAC-SHA256(key: UTF8(secret), canonical))
/// </code>
/// </summary>
public static class RequestSigning
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string TimestampHeader = "X-Timestamp";
    public const string NonceHeader = "X-Nonce";
    public const string SignatureHeader = "X-Signature";

    public static string BuildCanonicalString(string method, string path, string timestamp, string nonce, ReadOnlySpan<byte> body) =>
        string.Join('\n',
            method.ToUpperInvariant(),
            path,
            timestamp,
            nonce,
            Convert.ToHexStringLower(SHA256.HashData(body)));

    public static byte[] ComputeSignature(string secret, string canonical) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(canonical));

    /// <summary>Convenience for clients: returns the four headers to send.</summary>
    public static IReadOnlyDictionary<string, string> CreateHeaders(
        string keyId, string secret, string method, string path, ReadOnlySpan<byte> body, DateTimeOffset now, string? nonce = null)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        nonce ??= Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var signature = Convert.ToBase64String(ComputeSignature(secret, BuildCanonicalString(method, path, timestamp, nonce, body)));

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ApiKeyHeader] = keyId,
            [TimestampHeader] = timestamp,
            [NonceHeader] = nonce,
            [SignatureHeader] = signature,
        };
    }
}
