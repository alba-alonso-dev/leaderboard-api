using System.Text;
using Leaderboard.Application.Abstractions.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Leaderboard.Infrastructure.Security;

/// <summary>Encrypts API key secrets with ASP.NET Core Data Protection (key ring stored outside the database).</summary>
internal sealed class ApiKeySecretProtector(IDataProtectionProvider provider) : IApiKeySecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Leaderboard.ApiKeys.Secret.v1");

    public byte[] Protect(string secret) => _protector.Protect(Encoding.UTF8.GetBytes(secret));

    public string Unprotect(byte[] ciphertext) => Encoding.UTF8.GetString(_protector.Unprotect(ciphertext));
}
