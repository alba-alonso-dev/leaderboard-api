using Leaderboard.Application.Abstractions.Persistence;
using Leaderboard.Application.Abstractions.Security;
using Leaderboard.Infrastructure.Persistence;
using Leaderboard.Infrastructure.Persistence.Repositories;
using Leaderboard.Infrastructure.ReadModels;
using Leaderboard.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Leaderboard.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Postgres";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .EnableRetryOnFailure(maxRetryCount: 3)
                .MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IGameApiKeyRepository, GameApiKeyRepository>();
        services.AddScoped<IScoreRepository, ScoreRepository>();
        services.AddScoped<ILeaderboardReadService, LeaderboardReadService>();

        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IApiKeySecretProtector, ApiKeySecretProtector>();

        var dataProtection = services.AddDataProtection().SetApplicationName("leaderboard-api");
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return services;
    }
}
