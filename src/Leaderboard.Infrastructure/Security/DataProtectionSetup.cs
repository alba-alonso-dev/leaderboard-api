using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Leaderboard.Infrastructure.Security;

/// <summary>Where the Data Protection key ring lives and how it is protected at rest.</summary>
public sealed class DataProtectionStorageOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Directory for the key ring (Docker Compose mounts a volume here). When empty, the key ring is stored in
    /// PostgreSQL (<c>data_protection_keys</c>), which suits hosts without persistent disks.
    /// </summary>
    public string? KeysPath { get; set; }

    /// <summary>
    /// Base64 256-bit key that encrypts the key ring with AES-256-GCM. Required in Production when the key ring is
    /// stored in the database, so a database leak alone does not expose the API key secrets.
    /// </summary>
    public string? KeyEncryptionKey { get; set; }

    public bool UsesDatabase => string.IsNullOrWhiteSpace(KeysPath);

    public byte[]? GetKeyEncryptionKey()
    {
        if (string.IsNullOrWhiteSpace(KeyEncryptionKey))
        {
            return null;
        }

        var key = new byte[32];
        if (!Convert.TryFromBase64String(KeyEncryptionKey.Trim(), key, out var written) || written != key.Length)
        {
            throw new InvalidOperationException(
                "DataProtection:KeyEncryptionKey must be a base64-encoded 256-bit key (e.g. `openssl rand -base64 32`).");
        }

        return key;
    }
}

public static class DataProtectionSetup
{
    public static IServiceCollection AddLeaderboardDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(DataProtectionStorageOptions.SectionName);
        var options = section.Get<DataProtectionStorageOptions>() ?? new DataProtectionStorageOptions();
        services.Configure<DataProtectionStorageOptions>(section);

        var builder = services.AddDataProtection().SetApplicationName("leaderboard-api");
        if (options.UsesDatabase)
        {
            builder.PersistKeysToDbContext<Persistence.AppDbContext>();
        }
        else
        {
            builder.PersistKeysToFileSystem(new DirectoryInfo(options.KeysPath!));
        }

        if (options.GetKeyEncryptionKey() is { } key)
        {
            services.Configure<KeyManagementOptions>(o => o.XmlEncryptor = new AesGcmXmlEncryptor(key));
        }

        return services;
    }

    /// <summary>Fails startup when Production would store an unencrypted key ring next to the data it protects.</summary>
    public static void EnsureKeyRingIsProtected(DataProtectionStorageOptions options, bool isProduction)
    {
        if (isProduction && options.UsesDatabase && options.GetKeyEncryptionKey() is null)
        {
            throw new InvalidOperationException(
                "In Production the Data Protection key ring is stored in the database and must be encrypted: " +
                "set DataProtection:KeyEncryptionKey (base64, 256 bits) or DataProtection:KeysPath.");
        }
    }
}
