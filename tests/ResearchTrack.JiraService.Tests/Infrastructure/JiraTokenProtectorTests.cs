using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
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

    private static JiraTokenProtector CreateProtector(string key, IDataProtectionProvider legacyProvider) =>
        new(new JiraOptions { TokenEncryptionKey = key }, legacyProvider);

    private static IDataProtectionProvider LegacyProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private const string KeyA = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string KeyB = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
}
