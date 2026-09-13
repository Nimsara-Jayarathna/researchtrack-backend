using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Features.Installation;

public sealed class GitHubInstallationRepositoryServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RepositoryId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Available_repositories_are_scoped_to_installation_and_persisted_from_github()
    {
        var authorization = new StubAuthorizationClient();
        var store = new StubStore();
        var app = new StubGitHubAppClient();
        var repositoryClient = new StubRepositoryClient
        {
            Pages =
            {
                [1] = new GitHubInstallationRepositoryPage(
                    [Repository(101, "org/one", "one")],
                    2,
                    true),
                [2] = new GitHubInstallationRepositoryPage(
                    [Repository(202, "org/two", "two")],
                    2,
                    false)
            }
        };
        var service = CreateService(authorization, store, app, repositoryClient);

        var response = await service.TryGetAvailableAsync(
            UserId,
            SourceId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(1, authorization.CallCount);
        Assert.Equal(1, app.GetInstallationCallCount);
        Assert.Equal(1, app.CreateTokenCallCount);
        Assert.Equal(new[] { 1, 2 }, repositoryClient.ListedPages);
        Assert.Equal(new[] { 101L, 202L }, store.LastAvailableRepositories.Select(item => item.Id).Order().ToArray());
    }

    [Fact]
    public async Task Unauthorized_listing_stops_before_installation_token_or_github_repository_call()
    {
        var authorization = new StubAuthorizationClient
        {
            Failure = new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Forbidden")
        };
        var app = new StubGitHubAppClient();
        var repositoryClient = new StubRepositoryClient();
        var store = new StubStore();
        var service = CreateService(authorization, store, app, repositoryClient);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.TryGetAvailableAsync(
            UserId,
            SourceId,
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Equal(0, app.CreateTokenCallCount);
        Assert.Empty(repositoryClient.ListedPages);
        Assert.Equal(0, store.UpsertAvailableCallCount);
    }

    [Fact]
    public async Task Non_installation_source_returns_null_for_public_url_fallback()
    {
        var store = new StubStore { Source = null };
        var app = new StubGitHubAppClient();
        var service = CreateService(
            new StubAuthorizationClient(),
            store,
            app,
            new StubRepositoryClient());

        var response = await service.TryGetAvailableAsync(
            UserId,
            SourceId,
            TestContext.Current.CancellationToken);

        Assert.Null(response);
        Assert.Equal(0, app.CreateTokenCallCount);
    }

    [Fact]
    public async Task GitHub_listing_failure_does_not_persist_repository_inventory()
    {
        var store = new StubStore();
        var repositoryClient = new StubRepositoryClient
        {
            ListFailure = new ApiException(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.DependencyUnavailable,
                "GitHub unavailable")
        };
        var service = CreateService(
            new StubAuthorizationClient(),
            store,
            new StubGitHubAppClient(),
            repositoryClient);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.TryGetAvailableAsync(
            UserId,
            SourceId,
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal(0, store.UpsertAvailableCallCount);
    }

    [Fact]
    public async Task Legacy_installation_page_uses_the_same_available_repository_inventory()
    {
        var store = new StubStore();
        var repositoryClient = new StubRepositoryClient
        {
            Pages =
            {
                [1] = new GitHubInstallationRepositoryPage(
                    [
                        Repository(101, "org/one", "one"),
                        Repository(202, "org/two", "two")
                    ],
                    2,
                    false)
            }
        };
        var service = CreateService(
            new StubAuthorizationClient(),
            store,
            new StubGitHubAppClient(),
            repositoryClient);

        var page = await service.GetInstallationPageAsync(
            UserId,
            ProjectId,
            777,
            1,
            1,
            TestContext.Current.CancellationToken);

        Assert.Single(page.Items);
        Assert.Equal(2, page.TotalCount);
        Assert.True(page.HasNext);
        Assert.Equal(2, page.NextPage);
    }

    [Fact]
    public async Task Legacy_numeric_repository_selection_is_verified_before_becoming_internal_selection()
    {
        var store = new StubStore();
        var repositoryClient = new StubRepositoryClient
        {
            Repository = Repository(9001, "org/example", "example")
        };
        var service = CreateService(
            new StubAuthorizationClient(),
            store,
            new StubGitHubAppClient(),
            repositoryClient);

        var selection = await service.ResolveLegacySelectionAsync(
            UserId,
            ProjectId,
            777,
            9001,
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceId, selection.SourceId);
        Assert.Equal(RepositoryId, selection.RepositoryId);
        Assert.Equal(9001, selection.GitHubRepositoryId);
        Assert.Equal(1, store.UpsertVerifiedCallCount);
    }

    [Fact]
    public async Task Repository_removed_after_listing_is_rejected_before_persistence()
    {
        var store = new StubStore();
        var app = new StubGitHubAppClient();
        var repositoryClient = new StubRepositoryClient
        {
            GetFailure = new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "Removed")
        };
        var service = CreateService(
            new StubAuthorizationClient(),
            store,
            app,
            repositoryClient);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.TryVerifyForLinkAsync(
            UserId,
            ProjectId,
            SourceId,
            [new LinkGitHubRepositoryRequestItem(RepositoryId, null, true)],
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal(1, app.CreateTokenCallCount);
        Assert.Equal(1, repositoryClient.GetCallCount);
        Assert.Equal(0, store.UpsertVerifiedCallCount);
    }

    [Fact]
    public async Task Tampered_repository_selection_not_offered_by_source_never_reaches_github()
    {
        var store = new StubStore { Selection = null };
        var app = new StubGitHubAppClient();
        var repositoryClient = new StubRepositoryClient();
        var service = CreateService(
            new StubAuthorizationClient(),
            store,
            app,
            repositoryClient);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.TryVerifyForLinkAsync(
            UserId,
            ProjectId,
            SourceId,
            [new LinkGitHubRepositoryRequestItem(Guid.NewGuid(), null, true)],
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal(0, app.CreateTokenCallCount);
        Assert.Equal(0, repositoryClient.GetCallCount);
    }

    [Fact]
    public async Task Installation_source_cannot_be_reused_for_another_project()
    {
        var service = CreateService(
            new StubAuthorizationClient(),
            new StubStore(),
            new StubGitHubAppClient(),
            new StubRepositoryClient());

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.TryVerifyForLinkAsync(
            UserId,
            Guid.NewGuid(),
            SourceId,
            [new LinkGitHubRepositoryRequestItem(RepositoryId, null, true)],
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task Direct_installation_link_requires_exactly_one_repository()
    {
        var app = new StubGitHubAppClient();
        var repositoryClient = new StubRepositoryClient();
        var service = CreateService(
            new StubAuthorizationClient(),
            new StubStore(),
            app,
            repositoryClient);

        await Assert.ThrowsAsync<ApiValidationException>(() => service.TryVerifyForLinkAsync(
            UserId,
            ProjectId,
            SourceId,
            [
                new LinkGitHubRepositoryRequestItem(RepositoryId, null, true),
                new LinkGitHubRepositoryRequestItem(Guid.NewGuid(), null, false)
            ],
            TestContext.Current.CancellationToken));

        Assert.Equal(0, app.CreateTokenCallCount);
        Assert.Equal(0, repositoryClient.GetCallCount);
    }

    [Fact]
    public async Task Successful_link_verification_refreshes_authoritative_metadata()
    {
        var store = new StubStore();
        var repositoryClient = new StubRepositoryClient
        {
            Repository = Repository(9001, "org/renamed", "renamed")
        };
        var service = CreateService(
            new StubAuthorizationClient(),
            store,
            new StubGitHubAppClient(),
            repositoryClient);

        var handled = await service.TryVerifyForLinkAsync(
            UserId,
            ProjectId,
            SourceId,
            [new LinkGitHubRepositoryRequestItem(RepositoryId, "Display", true)],
            TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(1, store.UpsertVerifiedCallCount);
        Assert.Equal("org/renamed", store.LastVerifiedRepository?.FullName);
        Assert.Equal("main", store.LastVerifiedRepository?.DefaultBranch);
        Assert.Equal("https://github.com/org/renamed", store.LastVerifiedRepository?.HtmlUrl);
    }

    private static GitHubInstallationRepositoryService CreateService(
        StubAuthorizationClient authorization,
        StubStore store,
        StubGitHubAppClient app,
        StubRepositoryClient repositoryClient) => new(
            authorization,
            store,
            app,
            repositoryClient,
            new FixedTimeProvider(Now),
            NullLogger<GitHubInstallationRepositoryService>.Instance);

    private static GitHubInstallationRepository Repository(long id, string fullName, string name) =>
        new(id, "org", name, fullName, $"https://github.com/{fullName}", "main", true);

    private sealed class StubAuthorizationClient : IProjectAuthorizationClient
    {
        public Exception? Failure { get; init; }
        public int CallCount { get; private set; }

        public Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class StubStore : IInstallationRepositoryStore
    {
        public InstallationAccessSourceSnapshot? Source { get; init; } =
            new(SourceId, ProjectId, 777, "org", "ORG");
        public InstallationRepositorySelection? Selection { get; init; } =
            new(RepositoryId, 9001);
        public int UpsertAvailableCallCount { get; private set; }
        public int UpsertVerifiedCallCount { get; private set; }
        public IReadOnlyList<GitHubInstallationRepository> LastAvailableRepositories { get; private set; } =
            [];
        public GitHubInstallationRepository? LastVerifiedRepository { get; private set; }

        public Task<InstallationAccessSourceSnapshot?> GetSourceAsync(
            Guid sourceId,
            CancellationToken cancellationToken) => Task.FromResult(Source);

        public Task<InstallationAccessSourceSnapshot?> GetSourceByInstallationAsync(
            Guid projectId,
            long installationId,
            CancellationToken cancellationToken) => Task.FromResult(Source);

        public Task<GitHubAvailableRepositoriesResponse> UpsertAvailableAsync(
            Guid sourceId,
            IReadOnlyList<GitHubInstallationRepository> repositories,
            DateTime now,
            CancellationToken cancellationToken)
        {
            UpsertAvailableCallCount++;
            LastAvailableRepositories = repositories;
            var items = repositories.Select((repository, index) =>
                new GitHubRepositoryOptionResponse(
                    Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}"),
                    repository.Id,
                    repository.FullName,
                    repository.Name,
                    repository.OwnerLogin,
                    repository.DefaultBranch,
                    repository.HtmlUrl)).ToList();
            return Task.FromResult(new GitHubAvailableRepositoriesResponse(sourceId, items, items.Count));
        }

        public Task<InstallationRepositorySelection?> GetSelectionAsync(
            Guid sourceId,
            Guid repositoryId,
            CancellationToken cancellationToken) => Task.FromResult(Selection);

        public Task<Guid> UpsertVerifiedAsync(
            Guid sourceId,
            GitHubInstallationRepository repository,
            DateTime now,
            CancellationToken cancellationToken)
        {
            UpsertVerifiedCallCount++;
            LastVerifiedRepository = repository;
            return Task.FromResult(RepositoryId);
        }
    }

    private sealed class StubGitHubAppClient : IGitHubAppClient
    {
        public int GetInstallationCallCount { get; private set; }
        public int CreateTokenCallCount { get; private set; }

        public Task<GitHubInstallationInfo> GetInstallationAsync(
            long installationId,
            CancellationToken cancellationToken)
        {
            GetInstallationCallCount++;
            return Task.FromResult(new GitHubInstallationInfo(installationId, "org", "ORG"));
        }

        public Task<GitHubInstallationToken> CreateInstallationTokenAsync(
            long installationId,
            CancellationToken cancellationToken)
        {
            CreateTokenCallCount++;
            return Task.FromResult(new GitHubInstallationToken(
                "test-token",
                Now.AddMinutes(30).UtcDateTime));
        }
    }

    private sealed class StubRepositoryClient : IGitHubInstallationRepositoryClient
    {
        public Dictionary<int, GitHubInstallationRepositoryPage> Pages { get; } = [];
        public Exception? ListFailure { get; init; }
        public Exception? GetFailure { get; init; }
        public GitHubInstallationRepository Repository { get; init; } =
            GitHubInstallationRepositoryServiceTests.Repository(9001, "org/example", "example");
        public List<int> ListedPages { get; } = [];
        public int GetCallCount { get; private set; }

        public Task<GitHubInstallationRepositoryPage> ListAsync(
            GitHubInstallationToken token,
            int page,
            int perPage,
            CancellationToken cancellationToken)
        {
            ListedPages.Add(page);
            if (ListFailure is not null)
            {
                return Task.FromException<GitHubInstallationRepositoryPage>(ListFailure);
            }

            if (Pages.TryGetValue(page, out var result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(new GitHubInstallationRepositoryPage([], 0, false));
        }

        public Task<GitHubInstallationRepository> GetAsync(
            GitHubInstallationToken token,
            long repositoryId,
            CancellationToken cancellationToken)
        {
            GetCallCount++;
            return GetFailure is null
                ? Task.FromResult(Repository)
                : Task.FromException<GitHubInstallationRepository>(GetFailure);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
