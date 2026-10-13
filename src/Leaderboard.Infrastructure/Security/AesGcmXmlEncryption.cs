using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leaderboard.Infrastructure.Security;

/// <summary>Encrypts Data Protection key ring entries at rest with AES-256-GCM and a key from configuration.</summary>
public sealed class AesGcmXmlEncryptor(byte[] key) : IXmlEncryptor
{
    internal const string ElementName = "encryptedKey";
    internal const string Algorithm = "AES-256-GCM";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        CryptographicOperations.ZeroMemory(plaintext);
        var element = new XElement(
            ElementName,
            new XAttribute("algorithm", Algorithm),
            new XElement("nonce", Convert.ToBase64String(nonce)),
            new XElement("tag", Convert.ToBase64String(tag)),
            new XElement("value", Convert.ToBase64String(ciphertext)));

        return new EncryptedXmlInfo(element, typeof(AesGcmXmlDecryptor));
    }
}

/// <summary>Created by Data Protection through its activator; reads the key from <see cref="DataProtectionStorageOptions"/>.</summary>
public sealed class AesGcmXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var key = services.GetRequiredService<IOptions<DataProtectionStorageOptions>>().Value.GetKeyEncryptionKey()
                  ?? throw new InvalidOperationException(
                      "The key ring is encrypted but DataProtection:KeyEncryptionKey is not configured.");

        var nonce = Convert.FromBase64String(encryptedElement.Element("nonce")!.Value);
        var tag = Convert.FromBase64String(encryptedElement.Element("tag")!.Value);
        var ciphertext = Convert.FromBase64String(encryptedElement.Element("value")!.Value);
        var plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext); // throws if the key is wrong or the data was tampered with
        }

        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }
}
