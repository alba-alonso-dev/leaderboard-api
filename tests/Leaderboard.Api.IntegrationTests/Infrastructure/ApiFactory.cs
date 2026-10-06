using System.Data.Common;
using Leaderboard.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Leaderboard.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API (Program.cs) against a disposable PostgreSQL 18 container. Migrations run on startup exactly as in
/// Docker Compose. <see cref="ResetDatabaseAsync"/> wipes data between tests (schema is kept).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@leaderboard.test";
    public const string AdminPassword = "AdminPassw0rd!";

    /// <summary>Fixed 256-bit key so the key ring stored in the test database is encrypted, as in Production.</summary>
    public static readonly string KeyEncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("leaderboard")
        .WithUsername("leaderboard")
        .WithPassword("leaderboard-test")
        .Build();

    private Respawner? _respawner;

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Extra configuration for derived factories (e.g. rate limiting enabled).</summary>
    protected virtual IReadOnlyDictionary<string, string?> Overrides => new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Services; // start the host: applies migrations and seeds the admin

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            // The Data Protection key ring survives resets, like in a real deployment (hosts cache it in memory).
            TablesToIgnore = [new Respawn.Graph.Table("__ef_migrations_history"), new Respawn.Graph.Table("data_protection_keys")],
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
        await AdminSeeder.SeedAsync(Services, Services.GetRequiredService<IConfiguration>());
    }

    public async Task<T?> QueryScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using DbCommand command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task StopDatabaseAsync() => _postgres.StopAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,
            ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
            ["Database:MigrateOnStartup"] = "true",
            ["Swagger:Enabled"] = "true",
            ["DataProtection:KeyEncryptionKey"] = KeyEncryptionKey,
            ["RateLimiting:Enabled"] = "false",
            ["Scores:MaxPerPlayerPerMinute"] = "0",
            ["Scores:MaxImprovementFactor"] = "0", // enabled only in PlausibilityTests
            ["Seed:Admin:Email"] = AdminEmail,
            ["Seed:Admin:Password"] = AdminPassword,
            ["Serilog:MinimumLevel:Default"] = "Warning",
        };

        foreach (var (key, value) in Overrides)
        {
            settings[key] = value;
        }

        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
