using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Features.Webhooks;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.MutationTests.Features.Webhooks;

public sealed class GitHubWebhookEventProcessorCoverageTests
{
    [Fact]
    public async Task ProcessAsync_Ping_IsIgnoredWithoutDependencies()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        var inventory = Substitute.For<IGitHubInstallationRepositoryInventoryService>();
        var app = Substitute.For<IGitHubAppClient>();
        var sut = CreateSut(factory, inventory, app);

        var result = await sut.ProcessAsync(Delivery("ping", "{}"), ct);

        Assert.True(result.Ignored);
        Assert.Equal("ping", result.Reason);
        Assert.Empty(result.LinkedRepositoryIds);
        await app.DidNotReceive().GetInstallationAsync(Arg.Any<long>(), ct);
    }

    [Fact]
    public async Task ProcessAsync_UnsupportedEvent_IsIgnored()
    {
        var ct = CancellationToken.None;
        var sut = CreateSut(new TestFactory(), Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var result = await sut.ProcessAsync(Delivery("issues", "{}"), ct);

        Assert.True(result.Ignored);
        Assert.Equal("unsupported_event:issues", result.Reason);
    }

    [Fact]
    public async Task ProcessAsync_InvalidJson_ThrowsPermanentException()
    {
        var ct = CancellationToken.None;
        var sut = CreateSut(new TestFactory(), Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var exception = await Assert.ThrowsAsync<GitHubWebhookPermanentException>(() =>
            sut.ProcessAsync(Delivery("push", "{bad-json", installationId: 42), ct));

        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_PushWithoutInstallation_ThrowsPermanentException()
    {
        var ct = CancellationToken.None;
        var sut = CreateSut(new TestFactory(), Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var exception = await Assert.ThrowsAsync<GitHubWebhookPermanentException>(() =>
            sut.ProcessAsync(Delivery("push", RepositoryPayload("refs/heads/main")), ct));

        Assert.Contains("installation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_DefaultBranchPush_ReturnsLinkedRepositoryAndRefreshesMetadata()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        var ids = await SeedAsync(factory, ct);
        var sut = CreateSut(factory, Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var result = await sut.ProcessAsync(
            Delivery("push", RepositoryPayload("refs/heads/main", fullName: "owner/repo-renamed", name: "repo-renamed"), installationId: 42), ct);

        Assert.False(result.Ignored);
        Assert.Equal(new[] { ids.LinkId }, result.LinkedRepositoryIds);
        await using var db = factory.CreateDbContext();
        var repo = await db.Repositories.SingleAsync(x => x.Id == ids.RepositoryId, ct);
        var link = await db.ProjectRepositoryLinks.SingleAsync(x => x.Id == ids.LinkId, ct);
        Assert.Equal("owner/repo-renamed", repo.FullName);
        Assert.Equal("repo-renamed", link.Name);
    }

    [Fact]
    public async Task ProcessAsync_NonDefaultBranchPush_IsIgnored()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        await SeedAsync(factory, ct);
        var sut = CreateSut(factory, Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var result = await sut.ProcessAsync(
            Delivery("push", RepositoryPayload("refs/heads/feature/test"), installationId: 42), ct);

        Assert.True(result.Ignored);
        Assert.Equal("non_default_branch_or_unlinked_repository", result.Reason);
    }

    [Fact]
    public async Task ProcessAsync_PullRequestEvent_ForEnabledLink_QueuesRepositorySync()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        var ids = await SeedAsync(factory, ct);
        var sut = CreateSut(factory, Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var result = await sut.ProcessAsync(
            Delivery("pull_request", RepositoryPayload(), installationId: 42, action: "opened"), ct);

        Assert.False(result.Ignored);
        Assert.Contains(ids.LinkId, result.LinkedRepositoryIds);
    }

    [Fact]
    public async Task ProcessAsync_RepositoryDeleted_MarksInventoryUnavailable()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        var ids = await SeedAsync(factory, ct);
        var sut = CreateSut(factory, Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var result = await sut.ProcessAsync(
            Delivery("repository", RepositoryPayload(), installationId: 42, action: "deleted"), ct);

        Assert.True(result.Ignored);
        Assert.Equal("repository_deleted", result.Reason);
        await using var db = factory.CreateDbContext();
        Assert.False((await db.Repositories.SingleAsync(x => x.Id == ids.RepositoryId, ct)).Available);
    }

    [Fact]
    public async Task ProcessAsync_InstallationSuspend_MarksSourceSuspended()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        var ids = await SeedAsync(factory, ct);
        var sut = CreateSut(factory, Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var result = await sut.ProcessAsync(
            Delivery("installation", "{}", installationId: 42, action: "suspend"), ct);

        Assert.True(result.Ignored);
        Assert.Equal("installation_suspended", result.Reason);
        await using var db = factory.CreateDbContext();
        Assert.Equal(GitHubConnectionStatuses.Suspended, (await db.AccessSources.SingleAsync(x => x.Id == ids.SourceId, ct)).ConnectionStatus);
    }

    [Fact]
    public async Task ProcessAsync_InstallationDeleted_MarksSourceRemovedAndRepositoryUnavailable()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        var ids = await SeedAsync(factory, ct, activeInstallationKey: "key");
        var sut = CreateSut(factory, Substitute.For<IGitHubInstallationRepositoryInventoryService>(), Substitute.For<IGitHubAppClient>());

        var result = await sut.ProcessAsync(
            Delivery("installation", "{}", installationId: 42, action: "deleted"), ct);

        Assert.True(result.Ignored);
        await using var db = factory.CreateDbContext();
        var source = await db.AccessSources.SingleAsync(x => x.Id == ids.SourceId, ct);
        var repo = await db.Repositories.SingleAsync(x => x.Id == ids.RepositoryId, ct);
        Assert.Equal(GitHubConnectionStatuses.Removed, source.ConnectionStatus);
        Assert.Null(source.ActiveInstallationKey);
        Assert.False(repo.Available);
    }

    [Fact]
    public async Task ProcessAsync_InstallationCreated_StillSuspended_RemainsSuspendedWithoutInventoryRefresh()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        await SeedAsync(factory, ct);
        var inventory = Substitute.For<IGitHubInstallationRepositoryInventoryService>();
        var app = Substitute.For<IGitHubAppClient>();
        app.GetInstallationAsync(42, ct).Returns(new GitHubInstallationInfo(42, "owner", "User", Suspended: true));
        var sut = CreateSut(factory, inventory, app);

        var result = await sut.ProcessAsync(Delivery("installation", "{}", installationId: 42, action: "created"), ct);

        Assert.True(result.Ignored);
        Assert.Equal("installation_still_suspended", result.Reason);
        await inventory.DidNotReceive().TryRefreshAsync(Arg.Any<Guid>(), ct);
    }

    [Fact]
    public async Task ProcessAsync_InstallationCreated_RefreshesInventoryAndReturnsEnabledLinks()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        var ids = await SeedAsync(factory, ct, connectionStatus: GitHubConnectionStatuses.Suspended);
        var inventory = Substitute.For<IGitHubInstallationRepositoryInventoryService>();
        var app = Substitute.For<IGitHubAppClient>();
        app.GetInstallationAsync(42, ct).Returns(new GitHubInstallationInfo(42, "owner", "User"));
        inventory.TryRefreshAsync(ids.SourceId, ct).Returns((GitHubAvailableRepositoriesResponse?)null);
        var sut = CreateSut(factory, inventory, app);

        var result = await sut.ProcessAsync(Delivery("installation", "{}", installationId: 42, action: "created"), ct);

        Assert.False(result.Ignored);
        Assert.Contains(ids.LinkId, result.LinkedRepositoryIds);
        await inventory.Received(1).TryRefreshAsync(ids.SourceId, ct);
    }

    [Fact]
    public async Task ProcessAsync_InstallationRepositoriesRemoval_MarksRepositoryUnavailableAndRefreshesInventory()
    {
        var ct = CancellationToken.None;
        var factory = new TestFactory();
        var ids = await SeedAsync(factory, ct);
        var inventory = Substitute.For<IGitHubInstallationRepositoryInventoryService>();
        var sut = CreateSut(factory, inventory, Substitute.For<IGitHubAppClient>());
        var payload = """{"repositories_added":[],"repositories_removed":[{"id":1001}]}""";

        var result = await sut.ProcessAsync(Delivery("installation_repositories", payload, installationId: 42), ct);

        Assert.True(result.Ignored);
        Assert.Equal("repository_access_removed", result.Reason);
        await inventory.Received(1).TryRefreshAsync(ids.SourceId, ct);
        await using var db = factory.CreateDbContext();
        Assert.False((await db.Repositories.SingleAsync(x => x.Id == ids.RepositoryId, ct)).Available);
    }

    private static GitHubWebhookEventProcessor CreateSut(
        IDbContextFactory<GitHubDbContext> factory,
        IGitHubInstallationRepositoryInventoryService inventory,
        IGitHubAppClient app) =>
        new(factory, inventory, app, new FixedTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 30, 0, TimeSpan.Zero)), NullLogger<GitHubWebhookEventProcessor>.Instance);

    private static GitHubWebhookDelivery Delivery(
        string eventType,
        string payload,
        long? installationId = null,
        string? action = null) => new()
        {
            Id = Guid.NewGuid(),
            DeliveryId = Guid.NewGuid().ToString("N"),
            EventType = eventType,
            Action = action,
            InstallationId = installationId,
            GitHubRepositoryId = 1001,
            PayloadJson = payload,
            PayloadSha256 = "hash",
            Status = "RECEIVED",
            ReceivedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static string RepositoryPayload(
        string? reference = null,
        string fullName = "owner/repo",
        string name = "repo")
    {
        var refProperty = reference is null ? "" : $",\"ref\":\"{reference}\"";
        return $"{{\"repository\":{{\"id\":1001,\"name\":\"{name}\",\"full_name\":\"{fullName}\",\"html_url\":\"https://github.com/{fullName}\",\"default_branch\":\"main\",\"owner\":{{\"login\":\"owner\"}}}}{refProperty}}}";
    }

    private static async Task<SeedIds> SeedAsync(
        TestFactory factory,
        CancellationToken ct,
        string connectionStatus = GitHubConnectionStatuses.Connected,
        string? activeInstallationKey = null)
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
            InstallationId = 42,
            OwnerLogin = "owner",
            OwnerType = "User",
            AccessType = "APP",
            ConnectionStatus = connectionStatus,
            Active = true,
            ActiveInstallationKey = activeInstallationKey,
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
            Available = true,
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
        return new SeedIds(sourceId, repositoryId, linkId);
    }

    private sealed record SeedIds(Guid SourceId, Guid RepositoryId, Guid LinkId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestFactory : IDbContextFactory<GitHubDbContext>
    {
        private readonly DbContextOptions<GitHubDbContext> _options = new DbContextOptionsBuilder<GitHubDbContext>()
            .UseInMemoryDatabase($"github-webhook-processor-{Guid.NewGuid():N}")
            .Options;
        public GitHubDbContext CreateDbContext() => new(_options);
        public Task<GitHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
