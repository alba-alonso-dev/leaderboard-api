using System.Globalization;
using System.Security.Cryptography;
using Leaderboard.Application.Abstractions;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Games;
using Leaderboard.Domain.Scores;
using Microsoft.Extensions.Options;

namespace Leaderboard.Application.ApiKeys;

public sealed record SignedRequest(
    string? KeyId, string? Timestamp, string? Nonce, string? Signature, string Method, string Path, ReadOnlyMemory<byte> Body);

public sealed record AuthenticatedApiKey(Guid ApiKeyId, Guid GameId, string KeyId, string Nonce);

public interface IApiKeyAuthenticator
{
    Task<Result<AuthenticatedApiKey>> AuthenticateAsync(SignedRequest request, CancellationToken cancellationToken);
}

/// <summary>Verifies game-server requests: key exists and is active, timestamp is fresh, HMAC signature matches.</summary>
internal sealed class ApiKeyAuthenticator(
    IGameApiKeyRepository apiKeys,
    IApiKeySecretProtector secretProtector,
    IUnitOfWork unitOfWork,
    IOptions<ApiKeyOptions> options,
    TimeProvider timeProvider)
    : IApiKeyAuthenticator
{
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    public async Task<Result<AuthenticatedApiKey>> AuthenticateAsync(SignedRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.KeyId) || string.IsNullOrWhiteSpace(request.Timestamp) ||
            string.IsNullOrWhiteSpace(request.Nonce) || string.IsNullOrWhiteSpace(request.Signature) ||
            request.KeyId.Length > 64 || request.Nonce.Length is < Score.NonceMinLength or > Score.NonceMaxLength)
        {
            return ApiKeyErrors.Missing;
        }

        var now = timeProvider.GetUtcNow();
        if (!long.TryParse(request.Timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds) ||
            Math.Abs(now.ToUnixTimeSeconds() - unixSeconds) > options.Value.SignatureWindowSeconds)
        {
            return ApiKeyErrors.StaleRequest;
        }

        var apiKey = await apiKeys.GetActiveByKeyIdAsync(request.KeyId, cancellationToken);
        if (apiKey is null)
        {
            return ApiKeyErrors.Invalid;
        }

        var provided = new byte[32];
        if (!Convert.TryFromBase64String(request.Signature, provided, out var written) || written != provided.Length)
        {
            return ApiKeyErrors.InvalidSignature;
        }

        var secret = secretProtector.Unprotect(apiKey.SecretCiphertext);
        var canonical = RequestSigning.BuildCanonicalString(request.Method, request.Path, request.Timestamp, request.Nonce, request.Body.Span);
        var expected = RequestSigning.ComputeSignature(secret, canonical);

        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            return ApiKeyErrors.InvalidSignature;
        }

        if (apiKey.LastUsedAt is null || now - apiKey.LastUsedAt > LastUsedResolution)
        {
            apiKey.MarkUsed(now); // throttled to avoid a write on every request
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new AuthenticatedApiKey(apiKey.Id, apiKey.GameId, apiKey.KeyId, request.Nonce);
    }
}
