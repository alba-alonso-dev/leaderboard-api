using Leaderboard.Application.Abstractions;
using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Application.Auth;
using Leaderboard.Domain.Players;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Leaderboard.Application.UnitTests.Handlers;

public sealed class AuthHandlerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly IPlayerRepository _players = Substitute.For<IPlayerRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public AuthHandlerTests()
    {
        _hasher.Hash(Arg.Any<Player>(), Arg.Any<string>()).Returns("hashed");
        _tokens.CreateAccessToken(Arg.Any<Player>()).Returns(new AccessToken("jwt", _time.GetUtcNow().AddMinutes(15), 900));
        _tokens.CreateRefreshToken().Returns(("rt_new", new byte[] { 9 }));
        _tokens.HashRefreshToken(Arg.Any<string>()).Returns(ci => new byte[] { (byte)ci.Arg<string>().Length });
    }

    private TokenIssuer Issuer() => new(_tokens, _refreshTokens, Options.Create(new AuthOptions()), _time);

    [Fact]
    public async Task Register_WhenEmailTaken_ReturnsConflictWithoutSaving()
    {
        _players.EmailExistsAsync("ana@example.com", Arg.Any<CancellationToken>()).Returns(true);
        var sut = new RegisterPlayerCommandHandler(_players, _hasher, _unitOfWork, _time);

        var result = await sut.HandleAsync(new RegisterPlayerCommand("ana", "Ana@Example.com", "Sup3rSecret1"), CancellationToken.None);

        result.Error.ShouldBe(PlayerErrors.EmailTaken);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_WhenRaceLostOnUsername_ReturnsUsernameTaken()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new UniqueConstraintException(UniqueConstraints.PlayerUsername, new InvalidOperationException())));
        var sut = new RegisterPlayerCommandHandler(_players, _hasher, _unitOfWork, _time);

        var result = await sut.HandleAsync(new RegisterPlayerCommand("ana", "ana@example.com", "Sup3rSecret1"), CancellationToken.None);

        result.Error.ShouldBe(PlayerErrors.UsernameTaken);
    }

    [Fact]
    public async Task Register_StoresHashedPasswordOnly()
    {
        Player? added = null;
        _players.Add(Arg.Do<Player>(p => added = p));
        var sut = new RegisterPlayerCommandHandler(_players, _hasher, _unitOfWork, _time);

        var result = await sut.HandleAsync(new RegisterPlayerCommand("ana", "ana@example.com", "Sup3rSecret1"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        added.ShouldNotBeNull().PasswordHash.ShouldBe("hashed");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsSameErrorAsWrongPassword()
    {
        var player = Player.Register("ana", "ana@example.com", _time.GetUtcNow());
        player.SetPasswordHash("hash");
        _players.GetByEmailAsync("ana@example.com", Arg.Any<CancellationToken>()).Returns(player);
        _hasher.Verify(player, "hash", Arg.Any<string>()).Returns(false);
        var sut = new LoginCommandHandler(_players, _hasher, Issuer(), _unitOfWork);

        var unknown = await sut.HandleAsync(new LoginCommand("nobody@example.com", "whatever1"), CancellationToken.None);
        var wrongPassword = await sut.HandleAsync(new LoginCommand("ana@example.com", "whatever1"), CancellationToken.None);

        unknown.Error.ShouldBe(PlayerErrors.InvalidCredentials);
        wrongPassword.Error.ShouldBe(PlayerErrors.InvalidCredentials);
        _hasher.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IPasswordHasher.Verify)).ShouldBe(2);
    }

    [Fact]
    public async Task Refresh_WithRotatedToken_RevokesWholeFamily()
    {
        var family = Guid.CreateVersion7();
        var rotated = RefreshToken.Issue(Guid.CreateVersion7(), [1], _time.GetUtcNow(), TimeSpan.FromDays(7), family);
        rotated.Revoke(_time.GetUtcNow(), Guid.CreateVersion7());
        _refreshTokens.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(rotated);
        var sut = new RefreshTokenCommandHandler(_refreshTokens, _players, _tokens, Issuer(), _unitOfWork, _time);

        var result = await sut.HandleAsync(new RefreshTokenCommand("rt_stolen"), CancellationToken.None);

        result.Error.ShouldBe(PlayerErrors.InvalidRefreshToken);
        await _refreshTokens.Received(1).RevokeFamilyAsync(family, _time.GetUtcNow(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_WithActiveToken_RotatesWithinSameFamily()
    {
        var player = Player.Register("ana", "ana@example.com", _time.GetUtcNow());
        var current = RefreshToken.Issue(player.Id, [1], _time.GetUtcNow(), TimeSpan.FromDays(7));
        _refreshTokens.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(current);
        _players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        RefreshToken? replacement = null;
        _refreshTokens.Add(Arg.Do<RefreshToken>(t => replacement = t));
        var sut = new RefreshTokenCommandHandler(_refreshTokens, _players, _tokens, Issuer(), _unitOfWork, _time);

        var result = await sut.HandleAsync(new RefreshTokenCommand("rt_current"), CancellationToken.None);

        result.Value.RefreshToken.ShouldBe("rt_new");
        replacement.ShouldNotBeNull().FamilyId.ShouldBe(current.FamilyId);
        current.ReplacedById.ShouldBe(replacement.Id);
    }
}
