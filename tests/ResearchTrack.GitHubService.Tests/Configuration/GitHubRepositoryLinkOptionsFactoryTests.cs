using Microsoft.Extensions.Configuration;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Tests.Configuration;

public sealed class GitHubRepositoryLinkOptionsFactoryTests
{
    [Fact]
    public void Create_reads_required_environment_style_repository_limits()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            [GitHubRepositoryLinkOptionsFactory.MaxLinkedRepositoriesKey] = "8",
            [GitHubRepositoryLinkOptionsFactory.MaxEnabledRepositoriesKey] = "3"
        });

        var options = GitHubRepositoryLinkOptionsFactory.Create(configuration);

        Assert.Equal(8, options.MaxLinkedRepositories);
        Assert.Equal(3, options.MaxEnabledRepositories);
    }

    [Theory]
    [InlineData(null, "3")]
    [InlineData("", "3")]
    [InlineData("CHANGE_ME", "3")]
    [InlineData("0", "3")]
    [InlineData("five", "3")]
    [InlineData("5", null)]
    [InlineData("5", "0")]
    [InlineData("5", "many")]
    public void Create_rejects_missing_or_invalid_repository_limits(
        string? linked,
        string? enabled)
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            [GitHubRepositoryLinkOptionsFactory.MaxLinkedRepositoriesKey] = linked,
            [GitHubRepositoryLinkOptionsFactory.MaxEnabledRepositoriesKey] = enabled
        });

        Assert.Throws<InvalidOperationException>(() =>
            GitHubRepositoryLinkOptionsFactory.Create(configuration));
    }

    [Fact]
    public void Create_rejects_enabled_limit_greater_than_linked_limit()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            [GitHubRepositoryLinkOptionsFactory.MaxLinkedRepositoriesKey] = "2",
            [GitHubRepositoryLinkOptionsFactory.MaxEnabledRepositoriesKey] = "3"
        });

        Assert.Throws<InvalidOperationException>(() =>
            GitHubRepositoryLinkOptionsFactory.Create(configuration));
    }

    private static IConfiguration Build(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
