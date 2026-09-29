using Leaderboard.Domain.Players;
using Microsoft.AspNetCore.Identity;
using IPasswordHasher = Leaderboard.Application.Abstractions.Security.IPasswordHasher;

namespace Leaderboard.Infrastructure.Security;

/// <summary>PBKDF2 (HMAC-SHA512, 100k iterations) via ASP.NET Core Identity's hasher, without the rest of Identity.</summary>
internal sealed class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<Player> _hasher = new();

    public string Hash(Player player, string password) => _hasher.HashPassword(player, password);

    public bool Verify(Player player, string passwordHash, string providedPassword) =>
        _hasher.VerifyHashedPassword(player, passwordHash, providedPassword) != PasswordVerificationResult.Failed;
}
