using Microsoft.Extensions.Configuration;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Tests.Configuration;

public sealed class GitHubReconciliationOptionsFactoryTests
{
    [Fact]
    public void Create_UsesDefaultIntervalWhenNotConfigured()
    {
        var configuration = Build(new Dictionary<string, string?>());

        var options = GitHubReconciliationOptionsFactory.Create(configuration);

        Assert.Equal(
            TimeSpan.FromMinutes(GitHubReconciliationOptions.DefaultIntervalMinutes),
            options.Interval);
    }

    [Fact]
    public void Create_UsesEnvironmentStyleConfigurationValue()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["GitHub:SyncIntervalMinutes"] = "30"
        });

        var options = GitHubReconciliationOptionsFactory.Create(configuration);

        Assert.Equal(TimeSpan.FromMinutes(30), options.Interval);
    }

    [Fact]
    public void Create_AcceptsNestedReconciliationAliasForCompatibility()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["GitHub:Reconciliation:IntervalMinutes"] = "20"
        });

        var options = GitHubReconciliationOptionsFactory.Create(configuration);

        Assert.Equal(TimeSpan.FromMinutes(20), options.Interval);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1441")]
    public void Create_RejectsOutOfRangeIntervals(string value)
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["GitHub:SyncIntervalMinutes"] = value
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GitHubReconciliationOptionsFactory.Create(configuration));

        Assert.Contains("GitHub:SyncIntervalMinutes", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
