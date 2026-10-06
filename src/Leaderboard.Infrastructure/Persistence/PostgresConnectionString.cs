using Npgsql;

namespace Leaderboard.Infrastructure.Persistence;

/// <summary>
/// Accepts both Npgsql key/value connection strings and the <c>postgresql://user:pass@host:port/db?sslmode=require</c>
/// URIs shown by hosted providers (Neon, Render, Supabase…), so they can be pasted as-is into configuration.
/// </summary>
public static class PostgresConnectionString
{
    public static string Normalize(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var value = connectionString.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                builder.SslMode = Uri.UnescapeDataString(parts[1]).ToUpperInvariant() switch
                {
                    "DISABLE" => SslMode.Disable,
                    "ALLOW" => SslMode.Allow,
                    "PREFER" => SslMode.Prefer,
                    "VERIFY-CA" => SslMode.VerifyCA,
                    "VERIFY-FULL" => SslMode.VerifyFull,
                    _ => SslMode.Require,
                };
            }
        }

        return builder.ConnectionString;
    }
}
