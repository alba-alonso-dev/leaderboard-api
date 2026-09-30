using Leaderboard.Domain.Games;
using Leaderboard.Domain.Players;
using Leaderboard.Domain.Scores;
using Microsoft.EntityFrameworkCore;

namespace Leaderboard.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Game> Games => Set<Game>();

    public DbSet<GameApiKey> GameApiKeys => Set<GameApiKey>();

    public DbSet<Score> Scores => Set<Score>();

    public DbSet<LeaderboardEntry> LeaderboardEntries => Set<LeaderboardEntry>();

    internal DbSet<ReadModels.LeaderboardStats> LeaderboardStats => Set<ReadModels.LeaderboardStats>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
