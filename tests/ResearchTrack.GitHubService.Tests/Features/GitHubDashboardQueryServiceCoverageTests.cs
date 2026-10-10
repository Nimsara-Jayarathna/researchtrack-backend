using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Tests.Features;

public sealed class GitHubDashboardQueryServiceCoverageTests
{
    [Fact]
    public async Task GetDashboardAsync_NoRepositories_ReturnsDisconnectedIdleDashboard()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var factory = new Factory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var sut = new GitHubDashboardQueryService(factory, auth);

        var result = await sut.GetDashboardAsync(Guid.NewGuid(), projectId, null, ct);

        Assert.False(result.RepositoryLinked);
        Assert.Equal(0, result.AccessibleRepositoryCount.GetValueOrDefault());
        Assert.Equal("NOT_AUTHORIZED", result.AccessScope);
        Assert.Equal("idle", result.ActivitySummary.Status);
        Assert.Empty(result.Repositories);
        await auth.Received(1).EnsureCanViewAsync(projectId, ct);
    }

    [Fact]
    public async Task GetDashboardAsync_RequestedRepositoryNotInProject_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var factory = new Factory();
        await SeedRepositoryAsync(factory, projectId, primary: true, ct: ct);
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var sut = new GitHubDashboardQueryService(factory, auth);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            sut.GetDashboardAsync(Guid.NewGuid(), projectId, Guid.NewGuid(), ct));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetDashboardAsync_RichRepository_AggregatesCommitPullRequestContributorAndAccessState()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var factory = new Factory();
        var ids = await SeedRepositoryAsync(factory, projectId, primary: true, customName: "Research Repo", ct: ct);
        await using (var db = factory.CreateDbContext())
        {
            db.Commits.AddRange(
                Commit(ids.LinkId, "c1", "First", "alice", new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc)),
                Commit(ids.LinkId, "c2", "Second", "bob", new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc)));
            db.Contributors.AddRange(
                Contributor(ids.LinkId, 1, "alice", 3, "https://avatar/alice"),
                Contributor(ids.LinkId, 2, "bob", 1, null));
            db.PullRequests.AddRange(
                PullRequest(ids.LinkId, 11, "Open", "OPEN", false, false, new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc)),
                PullRequest(ids.LinkId, 12, "Draft", "OPEN", true, false, new DateTime(2026, 10, 10, 9, 20, 0, DateTimeKind.Utc)),
                PullRequest(ids.LinkId, 13, "Merged", "CLOSED", false, true, new DateTime(2026, 10, 10, 9, 10, 0, DateTimeKind.Utc)),
                PullRequest(ids.LinkId, 14, "Closed", "CLOSED", false, false, new DateTime(2026, 10, 10, 9, 5, 0, DateTimeKind.Utc)));
            db.AccessRequests.Add(new GitHubAccessRequest
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                ProjectTitle = "Research Project",
                TargetOwnerLogin = "owner",
                RequestedByUserId = Guid.NewGuid(),
                TokenNonce = "nonce",
                TokenHash = "hash",
                Status = GitHubAccessRequestStatuses.Completed,
                SourceId = ids.SourceId,
                InstallationId = 42,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                CompletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var sut = new GitHubDashboardQueryService(factory, auth);

        var result = await sut.GetDashboardAsync(Guid.NewGuid(), projectId, null, ct);

        Assert.True(result.RepositoryLinked);
        Assert.Equal(42L, result.AuthorizedInstallationId.GetValueOrDefault());
        Assert.Equal(1, result.AccessibleRepositoryCount.GetValueOrDefault());
        Assert.Equal("SINGLE_REPOSITORY", result.AccessScope);
        Assert.Equal("Research Repo", Assert.Single(result.Repositories).Name);
        Assert.Equal(2, result.ActivitySummary.TotalCommits);
        Assert.Equal(4, result.ActivitySummary.TotalPullRequests);
        Assert.Equal(1, result.ActivitySummary.OpenPullRequests);
        Assert.Equal(1, result.ActivitySummary.DraftPullRequests);
        Assert.Equal(1, result.ActivitySummary.MergedPullRequests);
        Assert.Equal(1, result.ActivitySummary.ClosedPullRequests);
        Assert.Equal("pull_request", result.ActivitySummary.LastActivityType);
        Assert.Equal(11, result.ActivitySummary.LastActivityPullRequestNumber);
        Assert.Equal("OPEN", result.ActivitySummary.LastActivityPullRequestStatus);
        Assert.Equal("active", result.ActivitySummary.Status);
        Assert.Equal(2, result.ContributorsPreview.Count);
        Assert.Equal(2, result.RecentCommitsPreview.Count);
        Assert.Equal(4, result.PullRequestsPreview.Count);
        Assert.True(result.HasUnacknowledgedAccess);
    }

    [Fact]
    public async Task GetDashboardAsync_MultipleRepositories_SelectsPrimaryAndReportsMultipleScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var factory = new Factory();
        var primary = await SeedRepositoryAsync(factory, projectId, primary: true, fullName: "owner/primary", ct: ct);
        await SeedRepositoryAsync(factory, projectId, primary: false, fullName: "owner/secondary", ct: ct);
        var sut = new GitHubDashboardQueryService(factory, Substitute.For<IProjectAuthorizationClient>());

        var result = await sut.GetDashboardAsync(Guid.NewGuid(), projectId, null, ct);

        Assert.True(result.RepositoryLinked);
        Assert.Equal(2, result.AccessibleRepositoryCount.GetValueOrDefault());
        Assert.Equal("MULTIPLE_REPOSITORIES", result.AccessScope);
        Assert.Equal("https://github.com/owner/primary", result.PrimaryRepositoryUrl);
        Assert.Contains(result.Repositories, repo => repo.Id == primary.LinkId);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetActivityAsync_InvalidPaging_FailsBeforeAuthorization(int page, int size)
    {
        var ct = TestContext.Current.CancellationToken;
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var sut = new GitHubDashboardQueryService(new Factory(), auth);

        await Assert.ThrowsAsync<ApiValidationException>(() =>
            sut.GetActivityAsync(Guid.NewGuid(), Guid.NewGuid(), null, page, size, ct));

        await auth.DidNotReceive().EnsureCanViewAsync(Arg.Any<Guid>(), ct);
    }

    [Fact]
    public async Task GetActivityAsync_PaginatesNewestCommitsAndMapsContributorAvatar()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var factory = new Factory();
        var ids = await SeedRepositoryAsync(factory, projectId, primary: true, ct: ct);
        await using (var db = factory.CreateDbContext())
        {
            for (var index = 1; index <= 3; index++)
            {
                db.Commits.Add(Commit(ids.LinkId, $"c{index}", $"Commit {index}", "alice", new DateTime(2026, 10, 10, 8, index, 0, DateTimeKind.Utc)));
            }
            db.Contributors.Add(Contributor(ids.LinkId, 1, "alice", 3, "https://avatar/alice"));
            await db.SaveChangesAsync(ct);
        }
        var sut = new GitHubDashboardQueryService(factory, Substitute.For<IProjectAuthorizationClient>());

        var page = await sut.GetActivityAsync(Guid.NewGuid(), projectId, ids.LinkId, 1, 2, ct);

        Assert.Equal(3, page.Total);
        Assert.True(page.HasMore);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("c3", page.Items[0].Sha);
        Assert.Equal("https://avatar/alice", page.Items[0].AvatarUrl);
    }

    [Fact]
    public async Task GetContributorsAsync_PaginatesByObservedCommitCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var factory = new Factory();
        var ids = await SeedRepositoryAsync(factory, projectId, primary: true, ct: ct);
        await using (var db = factory.CreateDbContext())
        {
            db.Contributors.AddRange(
                Contributor(ids.LinkId, 1, "alice", 5, null),
                Contributor(ids.LinkId, 2, "bob", 3, null),
                Contributor(ids.LinkId, 3, "charlie", 1, null));
            await db.SaveChangesAsync(ct);
        }
        var sut = new GitHubDashboardQueryService(factory, Substitute.For<IProjectAuthorizationClient>());

        var page = await sut.GetContributorsAsync(Guid.NewGuid(), projectId, ids.LinkId, 1, 2, ct);

        Assert.Equal(3, page.Total);
        Assert.True(page.HasMore);
        Assert.Equal(new[] { "alice", "bob" }, page.Items.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task GetDashboardAsync_EmptyProjectId_IsRejectedBeforeAuthorization()
    {
        var ct = TestContext.Current.CancellationToken;
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var sut = new GitHubDashboardQueryService(new Factory(), auth);

        await Assert.ThrowsAsync<ApiValidationException>(() =>
            sut.GetDashboardAsync(Guid.NewGuid(), Guid.Empty, null, ct));

        await auth.DidNotReceive().EnsureCanViewAsync(Arg.Any<Guid>(), ct);
    }

    private static async Task<SeedIds> SeedRepositoryAsync(
        Factory factory,
        Guid projectId,
        bool primary,
        string fullName = "owner/repo",
        string? customName = null,
        CancellationToken ct = default)
    {
        var sourceId = Guid.NewGuid();
        var repositoryId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var name = fullName[(fullName.IndexOf('/') + 1)..];
        await using var db = factory.CreateDbContext();
        db.AccessSources.Add(new GitHubAccessSource
        {
            Id = sourceId, ProjectId = projectId, CreatedByUserId = Guid.NewGuid(), InstallationId = 42,
            OwnerLogin = "owner", OwnerType = "User", AccessType = "APP", ConnectionStatus = GitHubConnectionStatuses.Connected,
            Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.Repositories.Add(new GitHubRepository
        {
            Id = repositoryId, SourceId = sourceId, GitHubRepositoryId = Random.Shared.NextInt64(1000, 9999),
            FullName = fullName, Name = name, OwnerLogin = "owner", DefaultBranch = "main",
            Url = $"https://github.com/{fullName}", Available = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            Id = linkId, ProjectId = projectId, SourceId = sourceId, GitHubRepositoryId = repositoryId,
            GitHubRepoId = Random.Shared.NextInt64(10000, 99999), LinkedByUserId = Guid.NewGuid(), AccessType = "APP",
            FullName = fullName, Name = name, CustomName = customName, OwnerLogin = "owner", DefaultBranch = "main",
            Url = $"https://github.com/{fullName}", Active = true, Enabled = true, Primary = primary,
            LinkedAt = DateTime.UtcNow, SyncStatus = GitHubSyncStatuses.Success, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return new SeedIds(sourceId, repositoryId, linkId);
    }

    private static GitHubCommit Commit(Guid linkId, string sha, string message, string login, DateTime committedAt) => new()
    {
        Id = Guid.NewGuid(), RepositoryLinkId = linkId, Sha = sha, Message = message, AuthorLogin = login,
        AuthorName = login.ToUpperInvariant(), HtmlUrl = $"https://example/{sha}", CommittedAt = committedAt,
        FirstSeenAt = committedAt, LastSeenAt = committedAt
    };

    private static GitHubContributor Contributor(Guid linkId, long userId, string login, int commits, string? avatar) => new()
    {
        Id = Guid.NewGuid(), RepositoryLinkId = linkId, GitHubUserId = userId, Login = login,
        AvatarUrl = avatar, GitHubContributionCount = commits, ObservedCommitCount = commits, LastSyncedAt = DateTime.UtcNow
    };

    private static GitHubPullRequest PullRequest(Guid linkId, int number, string title, string state, bool draft, bool merged, DateTime updatedAt) => new()
    {
        Id = Guid.NewGuid(), RepositoryLinkId = linkId, GitHubPullRequestId = number + 1000, Number = number,
        Title = title, State = state, IsDraft = draft, IsMerged = merged, AuthorLogin = "alice",
        SourceBranch = "feature", SourceSha = "source", TargetBranch = "main", TargetSha = "target",
        CreatedAt = updatedAt.AddHours(-1), UpdatedAt = updatedAt, HtmlUrl = $"https://example/pr/{number}", LastSyncedAt = updatedAt
    };

    private sealed record SeedIds(Guid SourceId, Guid RepositoryId, Guid LinkId);

    private sealed class Factory : IDbContextFactory<GitHubDbContext>
    {
        private readonly DbContextOptions<GitHubDbContext> _options = new DbContextOptionsBuilder<GitHubDbContext>()
            .UseInMemoryDatabase($"github-dashboard-{Guid.NewGuid():N}").Options;
        public GitHubDbContext CreateDbContext() => new(_options);
        public Task<GitHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
