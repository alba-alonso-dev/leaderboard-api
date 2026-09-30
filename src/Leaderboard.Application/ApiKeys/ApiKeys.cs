using System.Security.Cryptography;
using FluentValidation;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Application.Games;
using Leaderboard.Domain.Common;
using Leaderboard.Domain.Games;

namespace Leaderboard.Application.ApiKeys;

public sealed record ApiKeyResponse(
    Guid Id, string KeyId, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, bool IsActive)
{
    public static ApiKeyResponse From(GameApiKey key) =>
        new(key.Id, key.KeyId, key.Name, key.CreatedAt, key.LastUsedAt, key.RevokedAt, key.IsActive);
}

/// <summary>Only returned once, at creation time. The secret cannot be retrieved again.</summary>
public sealed record IssuedApiKeyResponse(Guid Id, string KeyId, string Name, string Secret, DateTimeOffset CreatedAt);

public sealed record IssueApiKeyCommand(Guid GameId, Guid RequesterId, string Name) : ICommand<IssuedApiKeyResponse>;

public sealed class IssueApiKeyCommandValidator : AbstractValidator<IssueApiKeyCommand>
{
    public IssueApiKeyCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(GameApiKey.NameMaxLength);
}

internal sealed class IssueApiKeyCommandHandler(
    IGameRepository games,
    IGameApiKeyRepository apiKeys,
    IApiKeySecretProtector secretProtector,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<IssueApiKeyCommand, IssuedApiKeyResponse>
{
    public const string SecretPrefix = "lbs_";

    public async Task<Result<IssuedApiKeyResponse>> HandleAsync(IssueApiKeyCommand command, CancellationToken cancellationToken)
    {
        var result = await games.GetOwnedAsync(command.GameId, command.RequesterId, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        if (result.Value.IsArchived)
        {
            return GameErrors.Archived;
        }

        if (await apiKeys.CountActiveAsync(command.GameId, cancellationToken) >= Game.MaxActiveApiKeys)
        {
            return ApiKeyErrors.LimitReached;
        }

        var keyId = GameApiKey.KeyIdPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var secret = SecretPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var apiKey = GameApiKey.Issue(command.GameId, keyId, command.Name, secretProtector.Protect(secret), timeProvider.GetUtcNow());

        apiKeys.Add(apiKey);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedApiKeyResponse(apiKey.Id, apiKey.KeyId, apiKey.Name, secret, apiKey.CreatedAt);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record ListApiKeysQuery(Guid GameId, Guid RequesterId) : IQuery<IReadOnlyList<ApiKeyResponse>>;

internal sealed class ListApiKeysQueryHandler(IGameRepository games, IGameApiKeyRepository apiKeys)
    : IQueryHandler<ListApiKeysQuery, IReadOnlyList<ApiKeyResponse>>
{
    public async Task<Result<IReadOnlyList<ApiKeyResponse>>> HandleAsync(ListApiKeysQuery query, CancellationToken cancellationToken)
    {
        var result = await games.GetOwnedAsync(query.GameId, query.RequesterId, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var keys = await apiKeys.ListByGameAsync(query.GameId, cancellationToken);
        return keys.Select(ApiKeyResponse.From).ToList();
    }
}

public sealed record RevokeApiKeyCommand(Guid GameId, Guid RequesterId, Guid ApiKeyId) : ICommand<Unit>;

internal sealed class RevokeApiKeyCommandHandler(
    IGameRepository games, IGameApiKeyRepository apiKeys, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<RevokeApiKeyCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken)
    {
        var result = await games.GetOwnedAsync(command.GameId, command.RequesterId, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var apiKey = await apiKeys.GetByIdAsync(command.GameId, command.ApiKeyId, cancellationToken);
        if (apiKey is null)
        {
            return ApiKeyErrors.NotFound;
        }

        apiKey.Revoke(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
