using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Domain.Players;

namespace Leaderboard.Api.Infrastructure;

/// <summary>Creates (or promotes) the administrator configured in <c>Seed:Admin</c>. Used for local/demo environments.</summary>
internal static partial class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct = default)
    {
        var email = configuration["Seed:Admin:Email"];
        var password = configuration["Seed:Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminSeeder));

        var admin = await players.GetByEmailAsync(email.Trim().ToLowerInvariant(), ct);
        if (admin is null)
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var username = configuration["Seed:Admin:Username"] ?? "admin";
            admin = Player.Register(username, email, scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
            admin.SetPasswordHash(hasher.Hash(admin, password));
            players.Add(admin);
        }

        if (!admin.IsAdmin)
        {
            admin.PromoteToAdmin();
            await unitOfWork.SaveChangesAsync(ct);
            AdminSeeded(logger, admin.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Administrator account {PlayerId} is ready")]
    private static partial void AdminSeeded(ILogger logger, Guid playerId);
}
