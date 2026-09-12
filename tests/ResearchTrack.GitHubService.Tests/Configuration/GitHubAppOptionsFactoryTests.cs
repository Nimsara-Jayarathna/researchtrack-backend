using Microsoft.Extensions.Configuration;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Tests.Configuration;

public sealed class GitHubAppOptionsFactoryTests
{
    [Fact]
    public void Valid_configuration_is_mapped_to_typed_options()
    {
        var configuration = BuildConfiguration();

        var options = GitHubAppOptionsFactory.Create(configuration);

        Assert.Equal(12345, options.AppId);
        Assert.Equal("researchtrack-test", options.AppSlug);
        Assert.Equal("Iv1.test-client", options.ClientId);
        Assert.Equal("test-client-secret", options.ClientSecret);
        Assert.Equal("/run/secrets/github-app.pem", options.PrivateKeyPath);
        Assert.Equal(TimeSpan.FromMinutes(10), options.StateLifetime);
    }

    [Theory]
    [InlineData("GitHub:AppId", "0")]
    [InlineData("GitHub:AppSlug", "CHANGE_ME")]
    [InlineData("GitHub:ClientId", "CHANGE_ME")]
    [InlineData("GitHub:ClientSecret", "CHANGE_ME")]
    [InlineData("GitHub:PrivateKeyPath", "CHANGE_ME")]
    [InlineData("GitHub:StateExpiryMinutes", "0")]
    [InlineData("GitHub:StateExpiryMinutes", "31")]
    [InlineData("GitHub:FrontendReturnOrigin", "https://app.example.test/path")]
    public void Invalid_configuration_is_rejected(string key, string value)
    {
        var values = ValidValues();
        values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Throws<InvalidOperationException>(() => GitHubAppOptionsFactory.Create(configuration));
    }

    [Fact]
    public void External_plain_http_urls_are_rejected()
    {
        var values = ValidValues();
        values["GitHub:SetupCallbackUrl"] = "http://example.test/callback";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Throws<InvalidOperationException>(() => GitHubAppOptionsFactory.Create(configuration));
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(ValidValues()).Build();

    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["GitHub:AppId"] = "12345",
        ["GitHub:AppSlug"] = "researchtrack-test",
        ["GitHub:ClientId"] = "Iv1.test-client",
        ["GitHub:ClientSecret"] = "test-client-secret",
        ["GitHub:PrivateKeyPath"] = "/run/secrets/github-app.pem",
        ["GitHub:SetupCallbackUrl"] = "https://api.example.test/api/github/access-source/install/callback",
        ["GitHub:FrontendReturnOrigin"] = "https://app.example.test",
        ["GitHub:StateExpiryMinutes"] = "10"
    };
}
