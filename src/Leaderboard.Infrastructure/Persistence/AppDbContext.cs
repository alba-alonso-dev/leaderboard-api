using Leaderboard.Domain.Games;
using Leaderboard.Domain.Players;
using Leaderboard.Domain.Scores;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Leaderboard.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Player> Players => Set<Player>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Game> Games => Set<Game>();

    public DbSet<GameApiKey> GameApiKeys => Set<GameApiKey>();

    public DbSet<Score> Scores => Set<Score>();

    public DbSet<LeaderboardEntry> LeaderboardEntries => Set<LeaderboardEntry>();

    internal DbSet<ReadModels.LeaderboardStats> LeaderboardStats => Set<ReadModels.LeaderboardStats>();

    /// <summary>ASP.NET Core Data Protection key ring (used when no file system path is configured).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
