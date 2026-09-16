using System.Net;
using System.Text;
using ResearchTrack.GitHubService.Features.Synchronization;

namespace ResearchTrack.GitHubService.Tests.Features.Synchronization;

public sealed class GitHubRepositorySyncClientTests
{
    [Fact]
    public async Task GetDefaultBranchHeadAsync_ReadsOnlyBranchHeadSha()
    {
        var handler = new StubHandler();
        var client = new GitHubRepositorySyncClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

        var result = await client.GetDefaultBranchHeadAsync(
            "research-owner",
            "research-repo",
            "main",
            "installation-token",
            TestContext.Current.CancellationToken);

        Assert.Equal("main", result.DefaultBranch);
        Assert.Equal("0123456789abcdef", result.HeadSha);
        Assert.Equal(
            "https://api.github.com/repos/research-owner/research-repo/branches/main",
            handler.LastRequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("installation-token", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task GetDefaultBranchHeadAsync_EncodesBranchNames()
    {
        var handler = new StubHandler();
        var client = new GitHubRepositorySyncClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

        await client.GetDefaultBranchHeadAsync(
            "research-owner",
            "research-repo",
            "release/v2",
            "installation-token",
            TestContext.Current.CancellationToken);

        Assert.Contains("branches/release%2Fv2", handler.LastRequestUri?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task GetPullRequestAsync_CapturesMergeActorAndForkSourceLabel()
    {
        var handler = new PullRequestStubHandler();
        var client = new GitHubRepositorySyncClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

        var result = await client.GetPullRequestAsync(
            "research-owner",
            "research-repo",
            29,
            "installation-token",
            TestContext.Current.CancellationToken);

        Assert.Equal(29, result.Number);
        Assert.True(result.Merged);
        Assert.Equal(987654321, result.MergedByGitHubId);
        Assert.Equal("merge-maintainer", result.MergedByLogin);
        Assert.Equal("student-fork:feature/SCRUM-19", result.SourceBranch);
        Assert.Equal("develop", result.TargetBranch);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"name\":\"main\",\"commit\":{\"sha\":\"0123456789abcdef\"}}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class PullRequestStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            const string payload = """
            {
              "id": 4532990150,
              "number": 29,
              "title": "Feature/SCRUM-19",
              "body": "## Jira\\n\\n- Jira item: SCRUM-19",
              "state": "closed",
              "draft": false,
              "merged": true,
              "user": {
                "id": 123456789,
                "login": "student-author"
              },
              "merged_by": {
                "id": 987654321,
                "login": "merge-maintainer"
              },
              "head": {
                "label": "student-fork:feature/SCRUM-19",
                "ref": "feature/SCRUM-19",
                "sha": "1111111111111111111111111111111111111111"
              },
              "base": {
                "ref": "develop",
                "sha": "2222222222222222222222222222222222222222"
              },
              "created_at": "2026-09-15T03:44:00Z",
              "updated_at": "2026-09-15T03:48:00Z",
              "closed_at": "2026-09-15T03:48:00Z",
              "merged_at": "2026-09-15T03:48:00Z",
              "merge_commit_sha": "3333333333333333333333333333333333333333",
              "html_url": "https://github.com/research-owner/research-repo/pull/29",
              "additions": 2842,
              "deletions": 711,
              "changed_files": 72,
              "commits": 9,
              "comments": 0,
              "review_comments": 0
            }
            """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
