using System.Text;
using Leaderboard.Application.Abstractions;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Application.ApiKeys;
using Leaderboard.Domain.Games;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Leaderboard.Application.UnitTests.ApiKeys;

public sealed class ApiKeyAuthenticatorTests
{
    private const string KeyId = "lbk_0123456789abcdef";
    private const string Secret = "lbs_super-secret-value";
    private const string Path = "/api/v1/games/1/scores";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"playerId":"p","value":10}""");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly IGameApiKeyRepository _repository = Substitute.For<IGameApiKeyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly GameApiKey _apiKey;
    private readonly ApiKeyAuthenticator _sut;

    public ApiKeyAuthenticatorTests()
    {
        var protector = new FakeProtector();
        _apiKey = GameApiKey.Issue(Guid.CreateVersion7(), KeyId, "prod", protector.Protect(Secret), _time.GetUtcNow());
        _repository.GetActiveByKeyIdAsync(KeyId, Arg.Any<CancellationToken>()).Returns(_apiKey);
        _sut = new ApiKeyAuthenticator(_repository, protector, _unitOfWork, Options.Create(new ApiKeyOptions()), _time);
    }

    private SignedRequest Signed(byte[]? body = null, string? secret = null, DateTimeOffset? at = null, string? nonce = null)
    {
        var headers = RequestSigning.CreateHeaders(KeyId, secret ?? Secret, "POST", Path, Body, at ?? _time.GetUtcNow(), nonce);
        return new SignedRequest(
            headers[RequestSigning.ApiKeyHeader], headers[RequestSigning.TimestampHeader], headers[RequestSigning.NonceHeader],
            headers[RequestSigning.SignatureHeader], "POST", Path, body ?? Body);
    }

    [Fact]
    public async Task Authenticate_WithValidSignature_ReturnsKeyAndGame()
    {
        var result = await _sut.AuthenticateAsync(Signed(nonce: "0123456789abcdef0123"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ApiKeyId.ShouldBe(_apiKey.Id);
        result.Value.GameId.ShouldBe(_apiKey.GameId);
        result.Value.Nonce.ShouldBe("0123456789abcdef0123");
        _apiKey.LastUsedAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task Authenticate_WhenBodyWasTampered_ReturnsInvalidSignature()
    {
        var result = await _sut.AuthenticateAsync(Signed(body: Encoding.UTF8.GetBytes("""{"playerId":"p","value":99999}""")), CancellationToken.None);

        result.Error.ShouldBe(ApiKeyErrors.InvalidSignature);
    }

    [Fact]
    public async Task Authenticate_WithWrongSecret_ReturnsInvalidSignature() =>
        (await _sut.AuthenticateAsync(Signed(secret: "lbs_wrong"), CancellationToken.None)).Error.ShouldBe(ApiKeyErrors.InvalidSignature);

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public async Task Authenticate_OutsideTimeWindow_ReturnsStaleRequest(int offsetSeconds) =>
        (await _sut.AuthenticateAsync(Signed(at: _time.GetUtcNow().AddSeconds(offsetSeconds)), CancellationToken.None))
            .Error.ShouldBe(ApiKeyErrors.StaleRequest);

    [Fact]
    public async Task Authenticate_WithUnknownOrRevokedKey_ReturnsInvalid()
    {
        _repository.GetActiveByKeyIdAsync(KeyId, Arg.Any<CancellationToken>()).Returns((GameApiKey?)null);

        (await _sut.AuthenticateAsync(Signed(), CancellationToken.None)).Error.ShouldBe(ApiKeyErrors.Invalid);
    }

    [Fact]
    public async Task Authenticate_WithMissingHeaders_ReturnsMissing() =>
        (await _sut.AuthenticateAsync(new SignedRequest(KeyId, null, null, null, "POST", Path, Body), CancellationToken.None))
            .Error.ShouldBe(ApiKeyErrors.Missing);

    [Fact]
    public async Task Authenticate_WithMalformedSignature_ReturnsInvalidSignature()
    {
        var request = Signed() with { Signature = "not base64!" };

        (await _sut.AuthenticateAsync(request, CancellationToken.None)).Error.ShouldBe(ApiKeyErrors.InvalidSignature);
    }

    [Fact]
    public async Task Authenticate_RecentlyUsedKey_DoesNotWriteAgain()
    {
        await _sut.AuthenticateAsync(Signed(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(10));
        await _sut.AuthenticateAsync(Signed(), CancellationToken.None);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CanonicalString_HasTheDocumentedShape()
    {
        var canonical = RequestSigning.BuildCanonicalString("post", "/p", "1", "n", []);

        canonical.ShouldBe("POST\n/p\n1\nn\ne3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    private sealed class FakeProtector : IApiKeySecretProtector
    {
        public byte[] Protect(string secret) => Encoding.UTF8.GetBytes(new string(secret.Reverse().ToArray()));

        public string Unprotect(byte[] ciphertext) => new(Encoding.UTF8.GetString(ciphertext).Reverse().ToArray());
    }
}
