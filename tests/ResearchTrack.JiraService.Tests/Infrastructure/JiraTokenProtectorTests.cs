using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Infrastructure;

namespace ResearchTrack.JiraService.Tests.Infrastructure;

public sealed class JiraTokenProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripsWithStableExternalKey()
    {
        var provider = LegacyProvider();
        var protector = CreateProtector(KeyA, provider);

        var encrypted = protector.Protect("jira-refresh-token");
        var decrypted = protector.Unprotect(encrypted);

        Assert.StartsWith("rtj:v2:", encrypted);
        Assert.DoesNotContain("jira-refresh-token", encrypted);
        Assert.Equal("jira-refresh-token", decrypted);
    }

    [Fact]
    public void Protect_SamePlaintext_ProducesDifferentCiphertextBecauseNonceIsRandom()
    {
        var protector = CreateProtector(KeyA, LegacyProvider());

        var first = protector.Protect("same-token");
        var second = protector.Protect("same-token");

        Assert.NotEqual(first, second);
        Assert.Equal("same-token", protector.Unprotect(first));
        Assert.Equal("same-token", protector.Unprotect(second));
    }

    [Fact]
    public void Ciphertext_CanBeReadByNewProtectorInstanceUsingSameKey()
    {
        var encrypted = CreateProtector(KeyA, LegacyProvider()).Protect("survives-container-replacement");
        var replacement = CreateProtector(KeyA, LegacyProvider());

        Assert.Equal("survives-container-replacement", replacement.Unprotect(encrypted));
    }

    [Fact]
    public void Ciphertext_CannotBeReadWithDifferentEnvironmentKey()
    {
        var encrypted = CreateProtector(KeyA, LegacyProvider()).Protect("test-environment-secret");
        var productionProtector = CreateProtector(KeyB, LegacyProvider());

        var error = Assert.Throws<JiraTokenProtectionException>(() => productionProtector.Unprotect(encrypted));

        Assert.Contains("Reconnect Jira", error.Message);
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_ThrowsReconnectFriendlyException()
    {
        var protector = CreateProtector(KeyA, LegacyProvider());
        var encrypted = protector.Protect("secret");
        var last = encrypted[^1];
        var tampered = encrypted[..^1] + (last == 'A' ? 'B' : 'A');

        var error = Assert.Throws<JiraTokenProtectionException>(() => protector.Unprotect(tampered));

        Assert.Contains("Reconnect Jira", error.Message);
    }

    [Fact]
    public void Unprotect_InvalidVersionedBase64_ThrowsReconnectFriendlyException()
    {
        var protector = CreateProtector(KeyA, LegacyProvider());

        var error = Assert.Throws<JiraTokenProtectionException>(() => protector.Unprotect("rtj:v2:not-base64!"));

        Assert.Contains("Reconnect Jira", error.Message);
    }

    [Fact]
    public void Constructor_InvalidBase64Key_ThrowsConfigurationException()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CreateProtector("not-base64!", LegacyProvider()));

        Assert.Contains("valid Base64", error.Message);
    }

    [Fact]
    public void Constructor_KeyThatIsNot32Bytes_ThrowsConfigurationException()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);

        var error = Assert.Throws<InvalidOperationException>(() => CreateProtector(shortKey, LegacyProvider()));

        Assert.Contains("exactly 32 bytes", error.Message);
    }

    [Fact]
    public void Unprotect_SupportsLegacyDataProtectionCiphertextDuringMigration()
    {
        var provider = LegacyProvider();
        var legacy = provider.CreateProtector("ResearchTrack.JiraService.OAuthTokens.v1");
        var oldCiphertext = legacy.Protect("legacy-refresh-token");
        var protector = CreateProtector(KeyA, provider);

        Assert.True(protector.RequiresReprotection(oldCiphertext));
        Assert.Equal("legacy-refresh-token", protector.Unprotect(oldCiphertext));
        Assert.False(protector.RequiresReprotection(protector.Protect("legacy-refresh-token")));
    }

    [Fact]
    public void Unprotect_LegacyCiphertextFromLostKeyRing_ThrowsReconnectFriendlyException()
    {
        var oldProvider = LegacyProvider();
        var oldCiphertext = oldProvider.CreateProtector("ResearchTrack.JiraService.OAuthTokens.v1").Protect("legacy-token");
        var replacement = CreateProtector(KeyA, new LostLegacyKeyRingProvider());

        var error = Assert.Throws<JiraTokenProtectionException>(() => replacement.Unprotect(oldCiphertext));

        Assert.Contains("Reconnect Jira", error.Message);
    }


    private sealed class LostLegacyKeyRingProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new LostLegacyKeyRingProtector();
    }

    private sealed class LostLegacyKeyRingProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext) => throw new NotSupportedException();

        public byte[] Unprotect(byte[] protectedData) =>
            throw new CryptographicException("The key was not found in the key ring.");
    }

    private static JiraTokenProtector CreateProtector(string key, IDataProtectionProvider legacyProvider) =>
        new(new JiraOptions { TokenEncryptionKey = key }, legacyProvider);

    private static IDataProtectionProvider LegacyProvider() =>
        new EphemeralDataProtectionProvider();

    private const string KeyA = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string KeyB = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
}
