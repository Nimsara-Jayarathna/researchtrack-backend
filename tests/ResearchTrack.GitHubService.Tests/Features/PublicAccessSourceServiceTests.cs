using Microsoft.AspNetCore.Http;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Features;

public sealed class PublicAccessSourceServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RepositoryId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Authorized_user_receives_frontend_available_repository_contract()
    {
        var authorization = new StubAuthorizationClient();
        var gitHub = new StubGitHubClient();
        var store = new StubStore();
        var service = CreateService(authorization, gitHub, store);

        var response = await service.CreateAsync(
            UserId,
            new CreatePublicAccessSourceRequest(ProjectId, "https://github.com/OpenAI/example.git"),
            TestContext.Current.CancellationToken);

        Assert.True(authorization.WasCalled);
        Assert.Equal("OpenAI", gitHub.Owner);
        Assert.Equal("example", gitHub.Repository);
        Assert.Equal(SourceId, response.SourceId);
        Assert.Equal(1, response.TotalCount);
        var item = Assert.Single(response.Items);
        Assert.Equal(RepositoryId, item.Id);
        Assert.Equal(1296269, item.GitHubRepoId);
        Assert.Equal("openai/example", item.FullName);
    }

    [Fact]
    public async Task Unauthorized_project_user_is_rejected_before_url_or_github_processing()
    {
        var authorization = new StubAuthorizationClient
        {
            Failure = new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Forbidden")
        };
        var gitHub = new StubGitHubClient();
        var service = CreateService(authorization, gitHub, new StubStore());

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            UserId,
            new CreatePublicAccessSourceRequest(ProjectId, "malformed"),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Null(gitHub.Owner);
    }

    [Fact]
    public async Task Duplicate_public_access_source_attempt_returns_conflict()
    {
        var store = new StubStore
        {
            Failure = new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "An active public access source already exists.")
        };
        var service = CreateService(new StubAuthorizationClient(), new StubGitHubClient(), store);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            UserId,
            new CreatePublicAccessSourceRequest(ProjectId, "https://github.com/openai/example"),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    private static PublicAccessSourceService CreateService(
        IProjectAuthorizationClient authorization,
        IGitHubPublicRepositoryClient gitHub,
        IPublicAccessSourceStore store) => new(
            authorization,
            gitHub,
            store,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

    private sealed class StubAuthorizationClient : IProjectAuthorizationClient
    {
        public Exception? Failure { get; init; }
        public bool WasCalled { get; private set; }

        public Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Assert.Equal(ProjectId, projectId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubGitHubClient : IGitHubPublicRepositoryClient
    {
        public string? Owner { get; private set; }
        public string? Repository { get; private set; }

        public Task<GitHubPublicRepository> GetAsync(
            string owner,
            string repository,
            CancellationToken cancellationToken)
        {
            Owner = owner;
            Repository = repository;
            return Task.FromResult(new GitHubPublicRepository(
                1296269,
                "openai",
                "ORG",
                "example",
                "openai/example",
                "https://github.com/openai/example",
                "main"));
        }
    }

    private sealed class StubStore : IPublicAccessSourceStore
    {
        public Exception? Failure { get; init; }

        public Task<GitHubAvailableRepositoriesResponse> CreateAsync(
            Guid projectId,
            Guid userId,
            GitHubPublicRepository repository,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException<GitHubAvailableRepositoriesResponse>(Failure);
            }

            Assert.Equal(ProjectId, projectId);
            Assert.Equal(UserId, userId);
            Assert.Equal(DateTimeKind.Utc, now.Kind);
            return Task.FromResult(new GitHubAvailableRepositoriesResponse(
                SourceId,
                [new GitHubRepositoryOptionResponse(
                    RepositoryId,
                    repository.Id,
                    repository.FullName,
                    repository.Name,
                    repository.OwnerLogin,
                    repository.DefaultBranch,
                    repository.HtmlUrl)],
                1));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
