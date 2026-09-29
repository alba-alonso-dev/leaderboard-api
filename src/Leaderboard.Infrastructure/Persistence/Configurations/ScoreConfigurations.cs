using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Domain.Games;
using Leaderboard.Domain.Players;
using Leaderboard.Domain.Scores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leaderboard.Infrastructure.Persistence.Configurations;

internal sealed class ScoreConfiguration : IEntityTypeConfiguration<Score>
{
    public void Configure(EntityTypeBuilder<Score> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Nonce).HasMaxLength(Score.NonceMaxLength).IsRequired();
        builder.Property(s => s.Metadata).HasColumnType("jsonb");
        builder.Property(s => s.Status).HasConversion<short>();

        builder.HasIndex(s => new { s.ApiKeyId, s.Nonce }).IsUnique().HasDatabaseName(UniqueConstraints.ScoreNonce);
        builder.HasIndex(s => new { s.PlayerId, s.GameId, s.SubmittedAt }).IsDescending(false, false, true);
        builder.HasIndex(s => s.GameId);

        builder.HasOne<Game>().WithMany().HasForeignKey(s => s.GameId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>().WithMany().HasForeignKey(s => s.PlayerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GameApiKey>().WithMany().HasForeignKey(s => s.ApiKeyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeaderboardEntryConfiguration : IEntityTypeConfiguration<LeaderboardEntry>
{
    public const string RankIndex = "ix_leaderboard_entries_rank";

    public void Configure(EntityTypeBuilder<LeaderboardEntry> builder)
    {
        builder.HasKey(e => new { e.GameId, e.PlayerId });

        // Serves Top N, pages, rank counting and the relative window: ORDER BY sort_key DESC, achieved_at, player_id.
        builder.HasIndex(e => new { e.GameId, e.SortKey, e.AchievedAt, e.PlayerId })
            .IsDescending(false, true, false, false)
            .HasDatabaseName(RankIndex);
        builder.HasIndex(e => e.PlayerId);
        builder.HasIndex(e => e.ScoreId);

        builder.HasOne<Game>().WithMany().HasForeignKey(e => e.GameId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>().WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Score>().WithMany().HasForeignKey(e => e.ScoreId).OnDelete(DeleteBehavior.Restrict);
    }
}
