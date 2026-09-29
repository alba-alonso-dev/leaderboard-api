using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Domain.Games;
using Leaderboard.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leaderboard.Infrastructure.Persistence.Configurations;

internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Slug).HasMaxLength(Game.SlugMaxLength).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(Game.NameMaxLength).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(Game.DescriptionMaxLength);
        builder.Property(g => g.ScoreOrder).HasConversion<short>();
        builder.Property(g => g.Version).IsRowVersion(); // PostgreSQL xmin system column

        builder.HasIndex(g => g.Slug).IsUnique().HasDatabaseName(UniqueConstraints.GameSlug);
        builder.HasIndex(g => g.OwnerId);
        builder.HasOne<Player>().WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GameApiKeyConfiguration : IEntityTypeConfiguration<GameApiKey>
{
    public void Configure(EntityTypeBuilder<GameApiKey> builder)
    {
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.KeyId).HasMaxLength(64).IsRequired();
        builder.Property(k => k.Name).HasMaxLength(GameApiKey.NameMaxLength).IsRequired();
        builder.Property(k => k.SecretCiphertext).IsRequired();
        builder.Ignore(k => k.IsActive);

        builder.HasIndex(k => k.KeyId).IsUnique();
        builder.HasIndex(k => k.GameId);
        builder.HasOne<Game>().WithMany().HasForeignKey(k => k.GameId).OnDelete(DeleteBehavior.Cascade);
    }
}
