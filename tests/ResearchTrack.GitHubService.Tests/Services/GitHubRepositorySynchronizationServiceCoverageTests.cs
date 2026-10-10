using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Tests.Services;

public sealed class GitHubRepositorySynchronizationServiceCoverageTests
{
    [Fact]
    public async Task SynchronizeAsync_MissingLink_ReturnsNotFoundBeforeGitHubCalls()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteGitHubDbContextFactory.CreateAsync(ct);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var app = Substitute.For<IGitHubAppClient>();
        var sut = CreateSut(factory, client, tokens, app);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.SynchronizeAsync(Guid.NewGuid(), "MANUAL", ct));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        await tokens.DidNotReceive().GetTokenAsync(Arg.Any<long>(), ct);
        await client.DidNotReceive().GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct);
    }

    [Fact]
    public async Task SynchronizeAsync_UnavailableRepository_IsSkippedWithoutExternalCalls()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteGitHubDbContextFactory.CreateAsync(ct);
        var ids = await SeedLinkedRepositoryAsync(factory, ct, repositoryAvailable: false);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var app = Substitute.For<IGitHubAppClient>();
        var sut = CreateSut(factory, client, tokens, app);

        var outcome = await sut.SynchronizeAsync(ids.LinkId, "MANUAL", ct);

        Assert.Equal(GitHubSynchronizationOutcome.SkippedUnavailable, outcome);
        await app.DidNotReceive().GetInstallationAsync(Arg.Any<long>(), ct);
        await tokens.DidNotReceive().GetTokenAsync(Arg.Any<long>(), ct);
    }

    [Fact]
    public async Task SynchronizeAsync_DisconnectedSource_IsSkippedWithoutExternalCalls()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteGitHubDbContextFactory.CreateAsync(ct);
        var ids = await SeedLinkedRepositoryAsync(factory, ct, connectionStatus: GitHubConnectionStatuses.Suspended);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var app = Substitute.For<IGitHubAppClient>();
        var sut = CreateSut(factory, client, tokens, app);

        var outcome = await sut.SynchronizeAsync(ids.LinkId, "RECONCILIATION", ct);

        Assert.Equal(GitHubSynchronizationOutcome.SkippedUnavailable, outcome);
        await client.DidNotReceive().GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct);
    }

    [Fact]
    public async Task SynchronizeAsync_MissingInstallation_FailsAndPersistsFailureState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteGitHubDbContextFactory.CreateAsync(ct);
        var ids = await SeedLinkedRepositoryAsync(factory, ct, installationId: null);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var app = Substitute.For<IGitHubAppClient>();
        var sut = CreateSut(factory, client, tokens, app);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.SynchronizeAsync(ids.LinkId, "MANUAL", ct));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        await using var db = factory.CreateDbContext();
        var link = await db.ProjectRepositoryLinks.SingleAsync(x => x.Id == ids.LinkId, ct);
        var run = await db.SyncRuns.SingleAsync(x => x.RepositoryLinkId == ids.LinkId, ct);
        Assert.Equal(GitHubSyncStatuses.Failed, link.SyncStatus);
        Assert.Equal(GitHubSyncRunStatuses.Failed, run.Status);
        Assert.NotNull(link.LastFailedSyncAt);
    }

    [Fact]
    public async Task SynchronizeAsync_SuspendedInstallation_FailsBeforeTokenOrRepositoryFetch()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteGitHubDbContextFactory.CreateAsync(ct);
        var ids = await SeedLinkedRepositoryAsync(factory, ct);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var app = Substitute.For<IGitHubAppClient>();
        app.GetInstallationAsync(42, ct).Returns(new GitHubInstallationInfo(42, "owner", "User", Suspended: true));
        var sut = CreateSut(factory, client, tokens, app);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.SynchronizeAsync(ids.LinkId, "MANUAL", ct));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        await tokens.DidNotReceive().GetTokenAsync(Arg.Any<long>(), ct);
        await client.DidNotReceive().GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), ct);
    }

    [Fact]
    public async Task SynchronizeAsync_RepositoryIdentityChanged_FailsAndRecordsError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteGitHubDbContextFactory.CreateAsync(ct);
        var ids = await SeedLinkedRepositoryAsync(factory, ct);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var app = Substitute.For<IGitHubAppClient>();
        app.GetInstallationAsync(42, ct).Returns(new GitHubInstallationInfo(42, "owner", "User"));
        tokens.GetTokenAsync(42, ct).Returns("token");
        client.GetRepositoryAsync("owner", "repo", "token", ct).Returns(Repository(id: 9999));
        var sut = CreateSut(factory, client, tokens, app);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.SynchronizeAsync(ids.LinkId, "WEBHOOK", ct));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        await using var db = factory.CreateDbContext();
        var link = await db.ProjectRepositoryLinks.SingleAsync(x => x.Id == ids.LinkId, ct);
        Assert.Equal(GitHubSyncStatuses.Failed, link.SyncStatus);
        Assert.Contains("identity", link.LastSyncError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyRemoteSnapshot_CompletesAndUpdatesLinkAndRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteGitHubDbContextFactory.CreateAsync(ct);
        var ids = await SeedLinkedRepositoryAsync(factory, ct);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var app = Substitute.For<IGitHubAppClient>();
        ConfigureHappyPath(app, tokens, client, ct);
        client.GetCommitsAsync("owner", "repo", "main", "token", ct).Returns(Array.Empty<GitHubSyncCommit>());
        client.GetContributorsAsync("owner", "repo", "token", ct).Returns(Array.Empty<GitHubSyncContributor>());
        client.GetPullRequestsAsync("owner", "repo", "token", ct).Returns(Array.Empty<GitHubSyncPullRequest>());
        var sut = CreateSut(factory, client, tokens, app);

        var outcome = await sut.SynchronizeAsync(ids.LinkId, "MANUAL", ct);

        Assert.Equal(GitHubSynchronizationOutcome.Completed, outcome);
        await using var db = factory.CreateDbContext();
        var link = await db.ProjectRepositoryLinks.SingleAsync(x => x.Id == ids.LinkId, ct);
        var run = await db.SyncRuns.SingleAsync(x => x.RepositoryLinkId == ids.LinkId, ct);
        Assert.Equal(GitHubSyncStatuses.Success, link.SyncStatus);
        Assert.Equal(1, link.SyncRevision);
        Assert.NotNull(link.LastSyncedAt);
        Assert.Equal(GitHubSyncRunStatuses.Success, run.Status);
        Assert.Equal(0, run.CommitsFetched);
        Assert.Equal(0, run.BranchesFetched);
    }

    [Fact]
    public async Task SynchronizeAsync_RichSnapshot_PersistsBranchCommitContributorPullRequestAndReview()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteGitHubDbContextFactory.CreateAsync(ct);
        var ids = await SeedLinkedRepositoryAsync(factory, ct);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var app = Substitute.For<IGitHubAppClient>();
        ConfigureHappyPath(app, tokens, client, ct);

        var summaryCommit = Commit(additions: null, deletions: null, changedFiles: null);
        var detailedCommit = Commit(additions: 12, deletions: 3, changedFiles: 2);
        var prSummary = PullRequest(updatedAt: new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc));
        var prDetail = prSummary with { Additions = 20, Deletions = 5, ChangedFiles = 4, Commits = 2, Comments = 1, ReviewComments = 1 };

        client.GetCommitsAsync("owner", "repo", "main", "token", ct).Returns(new[] { summaryCommit });
        client.GetCommitAsync("owner", "repo", "abc123", "token", ct).Returns(detailedCommit);
        client.GetContributorsAsync("owner", "repo", "token", ct).Returns(new[] {
            new GitHubSyncContributor(7, "alice", "https://avatar", "https://github.com/alice", 9)
        });
        client.GetPullRequestsAsync("owner", "repo", "token", ct).Returns(new[] { prSummary });
        client.GetPullRequestAsync("owner", "repo", 12, "token", ct).Returns(prDetail);
        client.GetPullRequestReviewsAsync("owner", "repo", 12, "token", ct).Returns(new[] {
            new GitHubSyncReview(501, 8, "reviewer", "APPROVED", new DateTime(2026, 10, 10, 8, 30, 0, DateTimeKind.Utc))
        });

        var sut = CreateSut(factory, client, tokens, app);
        var outcome = await sut.SynchronizeAsync(ids.LinkId, "WEBHOOK", ct);

        Assert.Equal(GitHubSynchronizationOutcome.Completed, outcome);
        await using var db = factory.CreateDbContext();
        var branch = await db.Branches.SingleAsync(x => x.RepositoryLinkId == ids.LinkId, ct);
        var commit = await db.Commits.SingleAsync(x => x.RepositoryLinkId == ids.LinkId, ct);
        var contributor = await db.Contributors.SingleAsync(x => x.RepositoryLinkId == ids.LinkId, ct);
        var pr = await db.PullRequests.SingleAsync(x => x.RepositoryLinkId == ids.LinkId, ct);
        var review = await db.PullRequestReviews.SingleAsync(x => x.PullRequestId == pr.Id, ct);
        var run = await db.SyncRuns.SingleAsync(x => x.RepositoryLinkId == ids.LinkId, ct);

        Assert.Equal("main", branch.Name);
        Assert.Equal("abc123", branch.HeadSha);
        Assert.Equal(12, commit.Additions);
        Assert.Equal(1, contributor.ObservedCommitCount);
        Assert.Equal(12, contributor.ObservedAdditions);
        Assert.Equal(20, pr.Additions);
        Assert.Equal("APPROVED", review.State);
        Assert.Equal(1, run.CommitsFetched);
        Assert.Equal(1, run.ContributorsFetched);
        Assert.Equal(1, run.PullRequestsFetched);
        Assert.Equal(1, run.ReviewsFetched);
        Assert.Equal(1, run.BranchesFetched);
    }

    private static GitHubRepositorySynchronizationService CreateSut(
        IDbContextFactory<GitHubDbContext> factory,
        IGitHubRepositorySyncClient client,
        IGitHubInstallationTokenProvider tokens,
        IGitHubAppClient app) =>
        new(factory, client, tokens, app, new FixedTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero)), NullLogger<GitHubRepositorySynchronizationService>.Instance);

    private static void ConfigureHappyPath(
        IGitHubAppClient app,
        IGitHubInstallationTokenProvider tokens,
        IGitHubRepositorySyncClient client,
        CancellationToken ct)
    {
        app.GetInstallationAsync(42, ct).Returns(new GitHubInstallationInfo(42, "owner", "User"));
        tokens.GetTokenAsync(42, ct).Returns("token");
        client.GetRepositoryAsync("owner", "repo", "token", ct).Returns(Repository());
    }

    private static GitHubSyncRepository Repository(long id = 1001) => new(
        id, "owner", "repo", "owner/repo", "https://github.com/owner/repo", "main",
        false, false, false, "repo", null, null, null);

    private static GitHubSyncCommit Commit(int? additions, int? deletions, int? changedFiles) => new(
        "abc123", "message", 7, "alice", "Alice", "alice@example.com", 7, "alice",
        new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 10, 7, 5, 0, DateTimeKind.Utc),
        "https://github.com/owner/repo/commit/abc123", 1, additions, deletions, changedFiles);

    private static GitHubSyncPullRequest PullRequest(DateTime updatedAt) => new(
        2001, 12, "Improve tests", "body", "OPEN", false, false, 7, "alice", null, null,
        "feature/tests", "source", "main", "target",
        new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc), updatedAt,
        null, null, null, "https://github.com/owner/repo/pull/12", null, null, null, null, null, null);

    private static async Task<SeedIds> SeedLinkedRepositoryAsync(
        SqliteGitHubDbContextFactory factory,
        CancellationToken ct,
        bool repositoryAvailable = true,
        string connectionStatus = GitHubConnectionStatuses.Connected,
        long? installationId = 42)
    {
        var projectId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var repositoryId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.AccessSources.Add(new GitHubAccessSource
        {
            Id = sourceId,
            ProjectId = projectId,
            CreatedByUserId = Guid.NewGuid(),
            InstallationId = installationId,
            OwnerLogin = "owner",
            OwnerType = "User",
            AccessType = "APP",
            ConnectionStatus = connectionStatus,
            Active = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Repositories.Add(new GitHubRepository
        {
            Id = repositoryId,
            SourceId = sourceId,
            GitHubRepositoryId = 1001,
            FullName = "owner/repo",
            Name = "repo",
            OwnerLogin = "owner",
            DefaultBranch = "main",
            Url = "https://github.com/owner/repo",
            Available = repositoryAvailable,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            Id = linkId,
            ProjectId = projectId,
            SourceId = sourceId,
            GitHubRepositoryId = repositoryId,
            GitHubRepoId = 1001,
            LinkedByUserId = Guid.NewGuid(),
            AccessType = "APP",
            FullName = "owner/repo",
            Name = "repo",
            OwnerLogin = "owner",
            DefaultBranch = "main",
            Url = "https://github.com/owner/repo",
            Active = true,
            Enabled = true,
            Primary = true,
            LinkedAt = DateTime.UtcNow,
            SyncStatus = GitHubSyncStatuses.Success,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return new SeedIds(projectId, sourceId, repositoryId, linkId);
    }

    private sealed record SeedIds(Guid ProjectId, Guid SourceId, Guid RepositoryId, Guid LinkId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SqliteGitHubDbContextFactory : IDbContextFactory<GitHubDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<GitHubDbContext> _options;

        private SqliteGitHubDbContextFactory(SqliteConnection connection)
        {
            _connection = connection;
            _options = new DbContextOptionsBuilder<GitHubDbContext>().UseSqlite(connection).Options;
        }

        public static async Task<SqliteGitHubDbContextFactory> CreateAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(ct);
            var factory = new SqliteGitHubDbContextFactory(connection);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync(ct);
            return factory;
        }

        public GitHubDbContext CreateDbContext() => new(_options);
        public Task<GitHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
