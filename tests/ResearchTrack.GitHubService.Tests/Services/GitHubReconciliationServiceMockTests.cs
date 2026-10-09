using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Tests.Services;

public sealed class GitHubReconciliationServiceMockTests
{
    [Fact]
    public async Task ReconcileAsync_ChangedHead_ObtainsTokenChecksRepositoryAndQueuesSyncOnce()
    {
        var factory = new TestGitHubDbContextFactory();
        var candidate = await SeedCandidateAsync(factory, "oldsha");
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var queue = Substitute.For<IRepositorySyncQueue>();
        tokens.GetTokenAsync(42, Arg.Any<CancellationToken>()).Returns("token");
        client.GetRepositoryAsync("owner", "repo", "token", Arg.Any<CancellationToken>()).Returns(
            new GitHubSyncRepository(1001, "owner", "repo", "owner/repo", "https://github.com/owner/repo", "main", false, false, false, null, null, null, null));
        client.GetDefaultBranchHeadAsync("owner", "repo", "main", "token", Arg.Any<CancellationToken>()).Returns(
            new GitHubDefaultBranchHead("main", "newsha"));
        var sut = new GitHubReconciliationService(factory, client, tokens, queue, NullLogger<GitHubReconciliationService>.Instance);

        var result = await sut.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.EligibleLinks);
        Assert.Equal(1, result.QueuedLinks);
        await tokens.Received(1).GetTokenAsync(42, Arg.Any<CancellationToken>());
        await client.Received(1).GetRepositoryAsync("owner", "repo", "token", Arg.Any<CancellationToken>());
        await queue.Received(1).EnqueueAsync(
            Arg.Is<RepositorySyncWorkItem>(x => x.LinkedRepositoryId == candidate && x.Trigger == GitHubSyncTriggers.Reconciliation),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileAsync_UnchangedHead_DoesNotQueue()
    {
        var factory = new TestGitHubDbContextFactory();
        await SeedCandidateAsync(factory, "same");
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var queue = Substitute.For<IRepositorySyncQueue>();
        tokens.GetTokenAsync(42, Arg.Any<CancellationToken>()).Returns("token");
        client.GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new GitHubSyncRepository(1001, "owner", "repo", "owner/repo", "https://github.com/owner/repo", "main", false, false, false, null, null, null, null));
        client.GetDefaultBranchHeadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new GitHubDefaultBranchHead("main", "same"));
        var sut = new GitHubReconciliationService(factory, client, tokens, queue, NullLogger<GitHubReconciliationService>.Instance);

        var result = await sut.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, result.QueuedLinks);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task ReconcileAsync_RemoteFailure_IsRecordedAndDoesNotQueue()
    {
        var factory = new TestGitHubDbContextFactory();
        await SeedCandidateAsync(factory, "old");
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        var tokens = Substitute.For<IGitHubInstallationTokenProvider>();
        var queue = Substitute.For<IRepositorySyncQueue>();
        tokens.GetTokenAsync(42, Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<string>(new HttpRequestException("GitHub unavailable")));
        var sut = new GitHubReconciliationService(factory, client, tokens, queue, NullLogger<GitHubReconciliationService>.Instance);

        var result = await sut.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FailedChecks);
        Assert.Equal(0, result.QueuedLinks);
        await client.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, default, default);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    private static async Task<Guid> SeedCandidateAsync(TestGitHubDbContextFactory factory, string head)
    {
        var sourceId = Guid.NewGuid();
        var repositoryId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.AccessSources.Add(new GitHubAccessSource
        {
            Id = sourceId, ProjectId = Guid.NewGuid(), CreatedByUserId = Guid.NewGuid(), InstallationId = 42,
            OwnerLogin = "owner", OwnerType = "User", AccessType = "APP", ConnectionStatus = GitHubConnectionStatuses.Connected,
            Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.Repositories.Add(new GitHubRepository
        {
            Id = repositoryId, SourceId = sourceId, GitHubRepositoryId = 1001, FullName = "owner/repo", Name = "repo",
            OwnerLogin = "owner", DefaultBranch = "main", Url = "https://github.com/owner/repo", Available = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            Id = linkId, ProjectId = Guid.NewGuid(), SourceId = sourceId, GitHubRepositoryId = repositoryId,
            GitHubRepoId = 1001, LinkedByUserId = Guid.NewGuid(), AccessType = "APP", FullName = "owner/repo", Name = "repo",
            OwnerLogin = "owner", DefaultBranch = "main", Url = "https://github.com/owner/repo", Active = true, Enabled = true,
            Primary = true, LinkedAt = DateTime.UtcNow, LastKnownHeadSha = head, SyncStatus = GitHubSyncStatuses.Success,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return linkId;
    }

    private sealed class TestGitHubDbContextFactory : IDbContextFactory<GitHubDbContext>
    {
        private readonly DbContextOptions<GitHubDbContext> _options = new DbContextOptionsBuilder<GitHubDbContext>()
            .UseInMemoryDatabase($"github-reconciliation-mocks-{Guid.NewGuid():N}").Options;
        public GitHubDbContext CreateDbContext() => new(_options);
        public Task<GitHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
