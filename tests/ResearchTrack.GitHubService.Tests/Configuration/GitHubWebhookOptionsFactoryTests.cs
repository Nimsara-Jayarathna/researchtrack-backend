using Microsoft.Extensions.Configuration;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Tests.Configuration;

public sealed class GitHubWebhookOptionsFactoryTests
{
    [Fact]
    public void Create_RequiresSecret()
    {
        var configuration = Build(new Dictionary<string, string?>());
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GitHubWebhookOptionsFactory.Create(configuration));
        Assert.Contains("GitHub:WebhookSecret", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("CHANGE_ME")]
    public void Create_RejectsWeakOrPlaceholderSecret(string secret)
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["GitHub:WebhookSecret"] = secret
        });
        Assert.Throws<InvalidOperationException>(() => GitHubWebhookOptionsFactory.Create(configuration));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    public void Create_RejectsInvalidPollInterval(string value)
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["GitHub:WebhookSecret"] = "0123456789abcdef0123456789abcdef0123456789abcdef",
            ["GitHub:Webhook:PollIntervalSeconds"] = value
        });

        Assert.Throws<InvalidOperationException>(() => GitHubWebhookOptionsFactory.Create(configuration));
    }

    [Fact]
    public void Create_UsesValidatedDefaults()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["GitHub:WebhookSecret"] = "0123456789abcdef0123456789abcdef0123456789abcdef"
        });
        var options = GitHubWebhookOptionsFactory.Create(configuration);
        Assert.Equal(GitHubWebhookOptions.DefaultMaxPayloadBytes, options.MaxPayloadBytes);
        Assert.Equal(GitHubWebhookOptions.DefaultMaxAttempts, options.MaxAttempts);
        Assert.Equal(GitHubWebhookOptions.DefaultProcessingLease, options.ProcessingLease);
        Assert.Equal(GitHubWebhookOptions.DefaultPollInterval, options.PollInterval);
    }

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
