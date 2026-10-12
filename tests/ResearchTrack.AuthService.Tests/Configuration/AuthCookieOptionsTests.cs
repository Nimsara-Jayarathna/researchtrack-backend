using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ResearchTrack.AuthService.Configuration;

namespace ResearchTrack.AuthService.Tests.Configuration;

public sealed class AuthCookieOptionsTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void FromConfiguration_RejectsInsecureCookiesOutsideDevelopment(string environmentName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Read(environmentName, "false"));

        Assert.Contains("Cookie:Secure must be true", exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void FromConfiguration_DefaultsToSecureCookiesOutsideDevelopment(string environmentName)
    {
        Assert.True(Read(environmentName, null).Secure);
        Assert.True(Read(environmentName, "").Secure);
        Assert.True(Read(environmentName, "true").Secure);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void FromConfiguration_AllowsInsecureCookiesInDevelopmentAndTests(string environmentName)
    {
        Assert.False(Read(environmentName, "false").Secure);
        Assert.False(Read(environmentName, null).Secure);
        Assert.True(Read(environmentName, "true").Secure);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void FromConfiguration_RejectsInvalidSecureSetting(string environmentName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Read(environmentName, "invalid"));

        Assert.Contains("must be true or false", exception.Message);
    }

    private static AuthCookieOptions Read(string environmentName, string? secure)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cookie:Secure"] = secure
            })
            .Build();

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        return AuthCookieOptions.FromConfiguration(configuration, environment);
    }
}
