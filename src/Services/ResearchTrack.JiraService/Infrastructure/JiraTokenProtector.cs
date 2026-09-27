using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using ResearchTrack.JiraService.Configuration;

namespace ResearchTrack.JiraService.Infrastructure;

public sealed class JiraTokenProtectionException : CryptographicException
{
    public JiraTokenProtectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class JiraTokenProtector : IJiraTokenProtector
{
    private const string VersionPrefix = "rtj:v2:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("ResearchTrack.JiraService.OAuthTokens.v2");

    private readonly byte[] _key;
    private readonly IDataProtector _legacyProtector;

    public JiraTokenProtector(JiraOptions options, IDataProtectionProvider legacyProvider)
    {
        _key = DecodeKey(options.TokenEncryptionKey);
        _legacyProtector = legacyProvider.CreateProtector("ResearchTrack.JiraService.OAuthTokens.v1");
    }

    public string Protect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var plaintext = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
        }

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);

        return VersionPrefix + Convert.ToBase64String(payload);
    }

    public bool RequiresReprotection(string protectedValue) =>
        !protectedValue.StartsWith(VersionPrefix, StringComparison.Ordinal);

    public string Unprotect(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!value.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            // Transitional compatibility for OAuth credentials written by deployments that
            // used ASP.NET Core Data Protection. New credentials are never written in this
            // format. If the old key ring has already been lost, the caller receives a
            // reauthorization-required error instead of an opaque key-ring exception.
            try
            {
                return _legacyProtector.Unprotect(value);
            }
            catch (CryptographicException ex)
            {
                throw new JiraTokenProtectionException(
                    "Jira authorization can no longer be decrypted. Reconnect Jira to continue synchronization.",
                    ex);
            }
        }

        try
        {
            var payload = Convert.FromBase64String(value[VersionPrefix.Length..]);
            if (payload.Length < NonceSize + TagSize)
                throw new CryptographicException("Encrypted Jira credential payload is incomplete.");

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var ciphertext = payload.AsSpan(NonceSize + TagSize);
            var plaintext = new byte[ciphertext.Length];

            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new JiraTokenProtectionException(
                "Jira authorization can no longer be decrypted. Reconnect Jira to continue synchronization.",
                ex);
        }
    }

    internal static byte[] DecodeKey(string encodedKey)
    {
        try
        {
            var key = Convert.FromBase64String(encodedKey);
            if (key.Length != 32)
                throw new InvalidOperationException("Jira__TokenEncryptionKey must decode to exactly 32 bytes (AES-256).");
            return key;
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Jira__TokenEncryptionKey must be a valid Base64-encoded 32-byte key.", ex);
        }
    }
}
