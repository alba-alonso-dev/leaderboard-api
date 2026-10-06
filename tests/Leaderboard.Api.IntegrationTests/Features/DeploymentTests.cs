using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Infrastructure.Persistence;
using Leaderboard.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Leaderboard.Api.IntegrationTests.Features;

/// <summary>Configuration needed to run on hosts without persistent disks behind a TLS proxy (e.g. Render + Neon).</summary>
public sealed class DeploymentTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task KeyRing_IsStoredInDatabase_Encrypted()
    {
        await Api.CreateGameAsync(); // issuing an API key uses Data Protection

        var xml = await Factory.QueryScalarAsync<string>("SELECT xml FROM data_protection_keys LIMIT 1");

        xml.ShouldNotBeNull();
        xml.ShouldContain("AES-256-GCM");
        XDocument.Parse(xml).Descendants("masterKey").Elements("value").ShouldBeEmpty(); // no plaintext master key
    }

    [Fact]
    public async Task ApiKeysIssuedBeforeARestart_KeepWorking()
    {
        var game = await Api.CreateGameAsync();

        // A brand-new host (same database, empty memory): it must load the key ring from PostgreSQL.
        using var restarted = new ApiDriver(Factory.WithWebHostBuilder(_ => { }).CreateClient());

        (await restarted.SubmitScoreAsync(game.Id, game.ApiKey, game.Owner.Id, 10)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}

/// <summary>Behind a reverse proxy every request comes from the proxy's IP unless X-Forwarded-For is honoured.</summary>
public sealed class ForwardedHeadersApiFactory : ApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Overrides => new Dictionary<string, string?>
    {
        ["FORWARDEDHEADERS_ENABLED"] = "true", // ASPNETCORE_FORWARDEDHEADERS_ENABLED in render.yaml
        ["RateLimiting:Enabled"] = "true",
        ["RateLimiting:AuthPerMinute"] = "2",
    };
}

public sealed class ForwardedHeadersTests(ForwardedHeadersApiFactory factory) : IClassFixture<ForwardedHeadersApiFactory>
{
    private static HttpRequestMessage Login(string clientIp) =>
        new(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = "x@example.com", password = "Whatever123" }),
            Headers = { { "X-Forwarded-For", clientIp } },
        };

    [Fact]
    public async Task RateLimits_ArePerClientIp_NotPerProxy()
    {
        using var client = factory.CreateClient();

        (await client.SendAsync(Login("203.0.113.10"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.SendAsync(Login("203.0.113.10"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.SendAsync(Login("203.0.113.10"))).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        (await client.SendAsync(Login("198.51.100.7"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

public sealed class DeploymentConfigurationTests
{
    private static readonly string ValidKey = Convert.ToBase64String(new byte[32]);

    [Theory]
    [InlineData("postgresql://neon_user:s3cr%40t@ep-cool-1.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require",
        "ep-cool-1.eu-central-1.aws.neon.tech", 5432, "neondb", "neon_user", "s3cr@t", SslMode.Require)]
    [InlineData("postgres://u:p@db.example.com:6543/leaderboard?sslmode=verify-full",
        "db.example.com", 6543, "leaderboard", "u", "p", SslMode.VerifyFull)]
    [InlineData("postgresql://u:p@localhost/leaderboard",
        "localhost", 5432, "leaderboard", "u", "p", SslMode.Prefer)]
    public void ConnectionString_FromUri_IsConvertedForNpgsql(
        string uri, string host, int port, string database, string user, string password, SslMode sslMode)
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(uri));

        builder.Host.ShouldBe(host);
        builder.Port.ShouldBe(port);
        builder.Database.ShouldBe(database);
        builder.Username.ShouldBe(user);
        builder.Password.ShouldBe(password);
        builder.SslMode.ShouldBe(sslMode);
    }

    [Fact]
    public void ConnectionString_InNpgsqlFormat_IsKeptAsIs()
    {
        const string value = "Host=localhost;Database=leaderboard;Username=u;Password=p";

        PostgresConnectionString.Normalize(value).ShouldBe(value);
    }

    [Fact]
    public void KeyRingEncryption_RoundTrips_AndDetectsTampering()
    {
        var options = new DataProtectionStorageOptions { KeyEncryptionKey = ValidKey };
        var services = new ServiceCollection().AddSingleton(Options.Create(options)).BuildServiceProvider();
        var original = new XElement("masterKey", new XElement("value", "very-secret"));

        var encrypted = new AesGcmXmlEncryptor(options.GetKeyEncryptionKey()!).Encrypt(original);
        encrypted.EncryptedElement.ToString().ShouldNotContain("very-secret");

        var decryptor = new AesGcmXmlDecryptor(services);
        decryptor.Decrypt(encrypted.EncryptedElement).ToString().ShouldBe(original.ToString());

        encrypted.EncryptedElement.Element("value")!.Value = Convert.ToBase64String(new byte[32]);
        Should.Throw<System.Security.Cryptography.AuthenticationTagMismatchException>(() => decryptor.Decrypt(encrypted.EncryptedElement));
    }

    [Theory]
    [InlineData(true, null, null, true)]           // Production + database + no key → refuse to start
    [InlineData(true, null, "key", false)]         // Production + database + key
    [InlineData(true, "/app/keys", null, false)]   // Production + file system (volume)
    [InlineData(false, null, null, false)]         // Development / tests
    public void ProductionKeyRing_MustBeProtected(bool isProduction, string? keysPath, string? key, bool shouldThrow)
    {
        var options = new DataProtectionStorageOptions { KeysPath = keysPath, KeyEncryptionKey = key is null ? null : ValidKey };

        var check = () => DataProtectionSetup.EnsureKeyRingIsProtected(options, isProduction);

        if (shouldThrow)
        {
            check.ShouldThrow<InvalidOperationException>();
        }
        else
        {
            check.ShouldNotThrow();
        }
    }

    [Fact]
    public void KeyEncryptionKey_MustBe256Bits() =>
        Should.Throw<InvalidOperationException>(() => new DataProtectionStorageOptions { KeyEncryptionKey = "c2hvcnQ=" }.GetKeyEncryptionKey());
}
