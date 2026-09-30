using System.Security.Claims;
using System.Text.Encodings.Web;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Application.ApiKeys;
using Leaderboard.Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Leaderboard.Api.Authentication;

/// <summary>
/// Authenticates game servers: <c>X-Api-Key</c> (public key id) + <c>X-Timestamp</c> + <c>X-Nonce</c> + <c>X-Signature</c>
/// (HMAC-SHA256 of the canonical request, see <see cref="RequestSigning"/>). The secret never travels over the wire.
/// </summary>
internal sealed partial class ApiKeyHmacAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const int MaxBodyBytes = 16 * 1024;
    private const string ErrorItemKey = "ApiKeyAuthenticationError";
    private static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789abcdef");

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers;
        if (!headers.TryGetValue(RequestSigning.ApiKeyHeader, out var keyId))
        {
            return Fail(Leaderboard.Domain.Games.ApiKeyErrors.Missing);
        }

        var body = await ReadBodyAsync();
        if (body is null)
        {
            return AuthenticateResult.Fail("Request body too large.");
        }

        var request = new SignedRequest(
            keyId.ToString(),
            headers[RequestSigning.TimestampHeader].ToString(),
            headers[RequestSigning.NonceHeader].ToString(),
            headers[RequestSigning.SignatureHeader].ToString(),
            Request.Method,
            Request.Path.Value ?? string.Empty,
            body);

        var result = await authenticator.AuthenticateAsync(request, Context.RequestAborted);
        if (result.IsFailure)
        {
            RejectedRequest(Logger, SafeKeyId(request.KeyId), result.Error!.Code);
            return Fail(result.Error!);
        }

        var key = result.Value;
        var identity = new ClaimsIdentity(
            [
                new Claim(LeaderboardClaims.AuthType, LeaderboardClaims.GameServer),
                new Claim(LeaderboardClaims.GameId, key.GameId.ToString()),
                new Claim(LeaderboardClaims.ApiKeyId, key.ApiKeyId.ToString()),
                new Claim(LeaderboardClaims.KeyId, key.KeyId),
                new Claim(LeaderboardClaims.Nonce, key.Nonce),
            ],
            Scheme.Name);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var error = Context.Items[ErrorItemKey] as Error ?? Leaderboard.Domain.Games.ApiKeyErrors.Missing;
        await error.ToProblem().ExecuteAsync(Context);
    }

    /// <summary>Never log a raw header: a misconfigured client may have put its secret in it.</summary>
    private static string SafeKeyId(string? keyId) =>
        keyId is { Length: 20 } && keyId.StartsWith(Leaderboard.Domain.Games.GameApiKey.KeyIdPrefix, StringComparison.Ordinal)
        && !keyId.AsSpan(4).ContainsAnyExcept(HexDigits)
            ? keyId
            : "(malformed)";

    private AuthenticateResult Fail(Error error)
    {
        Context.Items[ErrorItemKey] = error;
        return AuthenticateResult.Fail(error.Code);
    }

    /// <summary>Buffers the body so it can be hashed here and model-bound later by the endpoint.</summary>
    private async Task<byte[]?> ReadBodyAsync()
    {
        if (Request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        Request.EnableBuffering(bufferThreshold: MaxBodyBytes, bufferLimit: MaxBodyBytes);
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, Context.RequestAborted);
        Request.Body.Position = 0;
        return buffer.Length > MaxBodyBytes ? null : buffer.ToArray();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected game-server request with key {KeyId}: {ErrorCode}")]
    private static partial void RejectedRequest(ILogger logger, string keyId, string errorCode);
}
