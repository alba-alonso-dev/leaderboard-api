using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leaderboard.Infrastructure.Persistence.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Username).HasColumnType("citext").HasMaxLength(Player.UsernameMaxLength).IsRequired();
        builder.Property(p => p.Email).HasColumnType("citext").HasMaxLength(Player.EmailMaxLength).IsRequired();
        builder.Property(p => p.PasswordHash).IsRequired();
        builder.Property(p => p.Role).HasMaxLength(16).IsRequired();
        builder.Ignore(p => p.IsAdmin);

        builder.HasIndex(p => p.Email).IsUnique().HasDatabaseName(UniqueConstraints.PlayerEmail);
        builder.HasIndex(p => p.Username).IsUnique().HasDatabaseName(UniqueConstraints.PlayerUsername);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).IsRequired();
        builder.Ignore(t => t.WasRotated);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasOne<Player>().WithMany().HasForeignKey(t => t.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
