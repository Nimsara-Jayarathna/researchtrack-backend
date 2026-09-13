using System.Text.Json;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Infrastructure.GitHubApp;

public sealed class GitHubInstallationRepositoryClientTests
{
    [Fact]
    public async Task List_parses_safe_repository_metadata_and_pagination()
    {
        var api = new StubApiClient
        {
            Payload = """
                {
                  "total_count": 2,
                  "repositories": [
                    {
                      "id": 123,
                      "name": "repo",
                      "full_name": "org/repo",
                      "html_url": "https://github.com/org/repo",
                      "default_branch": "main",
                      "private": true,
                      "owner": { "login": "org" }
                    }
                  ]
                }
                """
        };
        var client = new GitHubInstallationRepositoryClient(api);

        var page = await client.ListAsync(
            Token(),
            1,
            1,
            TestContext.Current.CancellationToken);

        var repository = Assert.Single(page.Items);
        Assert.Equal(123, repository.Id);
        Assert.Equal("org/repo", repository.FullName);
        Assert.Equal("main", repository.DefaultBranch);
        Assert.True(repository.Private);
        Assert.True(page.HasNext);
        Assert.Equal("installation/repositories?per_page=1&page=1", api.LastPath);
    }

    [Fact]
    public async Task Get_fetches_repository_by_stable_github_id()
    {
        var api = new StubApiClient
        {
            Payload = """
                {
                  "id": 987,
                  "name": "repo",
                  "full_name": "org/repo",
                  "html_url": "https://github.com/org/repo",
                  "default_branch": "develop",
                  "private": false,
                  "owner": { "login": "org" }
                }
                """
        };
        var client = new GitHubInstallationRepositoryClient(api);

        var repository = await client.GetAsync(
            Token(),
            987,
            TestContext.Current.CancellationToken);

        Assert.Equal(987, repository.Id);
        Assert.Equal("develop", repository.DefaultBranch);
        Assert.Equal("repositories/987", api.LastPath);
    }

    private static GitHubInstallationToken Token() =>
        new("server-only-token", DateTime.UtcNow.AddMinutes(30));

    private sealed class StubApiClient : IGitHubInstallationApiClient
    {
        public required string Payload { get; init; }
        public string? LastPath { get; private set; }

        public Task<JsonDocument> GetAsync(
            string relativePath,
            GitHubInstallationToken token,
            CancellationToken cancellationToken)
        {
            LastPath = relativePath;
            return Task.FromResult(JsonDocument.Parse(Payload));
        }
    }
}
