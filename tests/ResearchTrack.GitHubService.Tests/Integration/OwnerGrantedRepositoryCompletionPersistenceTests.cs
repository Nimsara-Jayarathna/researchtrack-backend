using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;
using ResearchTrack.Testing;

namespace ResearchTrack.GitHubService.Tests.Integration;

public sealed class OwnerGrantedRepositoryCompletionPersistenceTests : IAsyncLifetime
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Now = new(2026, 9, 14, 8, 30, 0, DateTimeKind.Utc);
    private ResearchTrackWebApplicationFactory<Program>? _factory;

    public async ValueTask InitializeAsync()
    {
        var connectionString = TestDatabaseConfiguration.GetRequiredConnectionString("GITHUB");
        _factory = new ResearchTrackWebApplicationFactory<Program>(connectionString);

        await using var db = await CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Successful_completion_persists_request_source_metadata_link_and_one_sync_intent_atomically()
    {
        var request = await SeedRequestAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var store = CreateStore();

        var result = await store.CompleteAsync(
            request.Id,
            Installation(777),
            Repository(987654, "openai", "researchtrack"),
            Now,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.AlreadyCompleted);
        Assert.NotNull(result.InitialSyncRequest);

        await using var db = await CreateDbContextAsync();
        var persistedRequest = await db.RepositoryAccessRequests.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Completed, persistedRequest.Status);
        Assert.Equal(987654, persistedRequest.GitHubRepositoryId);
        Assert.Equal(Now, persistedRequest.CompletedAt);
        Assert.Equal(Now, persistedRequest.ConsumedAt);

        var source = await db.AccessSources.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GitHubAccessTypes.InstallationRequested, source.AccessType);
        Assert.Equal(777, source.InstallationId);

        var repository = await db.Repositories.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(987654, repository.GitHubRepositoryId);
        Assert.Equal("openai/researchtrack", repository.FullName);
        Assert.Equal("openai", repository.OwnerLogin);
        Assert.Equal("researchtrack", repository.Name);
        Assert.Equal("https://github.com/openai/researchtrack", repository.Url);
        Assert.Equal("main", repository.DefaultBranch);

        var link = await db.ProjectRepositoryLinks.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(GitHubAccessTypes.InstallationRequested, link.AccessType);
        Assert.Equal(987654, link.GitHubRepoId);
        Assert.True(link.Active);
        Assert.True(link.Enabled);
        Assert.True(link.Primary);
        Assert.Equal(GitHubSyncStatuses.Pending, link.SyncStatus);
        Assert.Equal(link.Id, result.InitialSyncRequest!.LinkedRepositoryId);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Duplicate_completion_returns_existing_connection_without_duplicate_rows_or_sync_intent()
    {
        var request = await SeedRequestAsync(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var store = CreateStore();
        var installation = Installation(777);
        var repository = Repository(987654, "openai", "researchtrack");

        var first = await store.CompleteAsync(
            request.Id, installation, repository, Now, TestContext.Current.CancellationToken);
        var second = await store.CompleteAsync(
            request.Id, installation, repository, Now.AddSeconds(1), TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.False(first.AlreadyCompleted);
        Assert.True(second.Succeeded);
        Assert.True(second.AlreadyCompleted);
        Assert.Null(second.InitialSyncRequest);
        Assert.Equal(first.SourceId, second.SourceId);
        Assert.Equal(first.LinkedRepositoryId, second.LinkedRepositoryId);

        await using var db = await CreateDbContextAsync();
        Assert.Equal(1, await db.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Repositories.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.ProjectRepositoryLinks.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.RepositoryAccessRequests.CountAsync(
            item => item.Status == GitHubRepositoryAccessRequestStatuses.Completed,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Concurrent_duplicate_completion_creates_one_logical_connection()
    {
        var request = await SeedRequestAsync(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var installation = Installation(777);
        var repository = Repository(987654, "openai", "researchtrack");

        async Task<OwnerGrantedRepositoryCompletionPersistenceResult> CompleteAsync() =>
            await CreateStore().CompleteAsync(
                request.Id,
                installation,
                repository,
                Now,
                TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(CompleteAsync(), CompleteAsync());

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Single(results, result => !result.AlreadyCompleted);
        Assert.Single(results, result => result.AlreadyCompleted);
        Assert.Single(results, result => result.InitialSyncRequest is not null);

        await using var db = await CreateDbContextAsync();
        Assert.Equal(1, await db.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Repositories.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.ProjectRepositoryLinks.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Already_linked_repository_fails_request_without_changing_existing_project_connection()
    {
        await SeedExistingLinkAsync(987654, "openai", "researchtrack");
        var request = await SeedRequestAsync(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        var result = await CreateStore().CompleteAsync(
            request.Id,
            Installation(777),
            Repository(987654, "openai", "researchtrack"),
            Now,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("repository_already_linked", result.ErrorCode);
        Assert.Null(result.InitialSyncRequest);

        await using var db = await CreateDbContextAsync();
        var persistedRequest = await db.RepositoryAccessRequests.SingleAsync(
            item => item.Id == request.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Failed, persistedRequest.Status);
        Assert.Equal("repository_already_linked", persistedRequest.FailureCode);
        Assert.Equal(1, await db.ProjectRepositoryLinks.CountAsync(
            item => item.Active,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Repositories.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Existing_different_repository_does_not_block_completion_when_configured_limits_allow_it()
    {
        var existingLinkId = await SeedExistingLinkAsync(111111, "existing", "repository");
        var request = await SeedRequestAsync(
            Guid.Parse("abababab-abab-abab-abab-abababababab"));

        var result = await CreateStore(maxLinked: 5, maxEnabled: 5).CompleteAsync(
            request.Id,
            Installation(777),
            Repository(987654, "openai", "researchtrack"),
            Now,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.AlreadyCompleted);

        await using var db = await CreateDbContextAsync();
        var links = await db.ProjectRepositoryLinks
            .Where(item => item.Active)
            .OrderBy(item => item.LinkedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, links.Count);
        Assert.Contains(links, item => item.Id == existingLinkId && item.GitHubRepoId == 111111);
        Assert.Contains(links, item => item.GitHubRepoId == 987654);
        Assert.Single(links, item => item.Primary);

        var persistedRequest = await db.RepositoryAccessRequests.SingleAsync(
            item => item.Id == request.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Completed, persistedRequest.Status);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Conflicting_installation_sources_fail_deterministically_without_creating_a_link()
    {
        var request = await SeedRequestAsync(
            Guid.Parse("acacacac-acac-acac-acac-acacacacacac"));
        await using (var db = await CreateDbContextAsync())
        {
            db.AccessSources.AddRange(
                InstallationSource(Guid.NewGuid(), 777),
                InstallationSource(Guid.NewGuid(), 777));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateStore().CompleteAsync(
            request.Id,
            Installation(777),
            Repository(987654, "openai", "researchtrack"),
            Now,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("installation_source_conflict", result.ErrorCode);
        Assert.Null(result.InitialSyncRequest);

        await using var verify = await CreateDbContextAsync();
        Assert.Equal(2, await verify.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.Repositories.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.ProjectRepositoryLinks.CountAsync(TestContext.Current.CancellationToken));
        var persistedRequest = await verify.RepositoryAccessRequests.SingleAsync(
            item => item.Id == request.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Failed, persistedRequest.Status);
        Assert.Equal("installation_source_conflict", persistedRequest.FailureCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Concurrent_project_limit_race_allows_only_one_new_repository_link()
    {
        var firstRequest = await SeedRequestAsync(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "openai",
            "one",
            701,
            'e');
        var secondRequest = await SeedRequestAsync(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            "openai",
            "two",
            702,
            'f');

        async Task<OwnerGrantedRepositoryCompletionPersistenceResult> CompleteAsync(
            GitHubRepositoryAccessRequest request,
            long installationId,
            long repositoryId,
            string name) => await CreateStore(maxLinked: 1, maxEnabled: 1).CompleteAsync(
                request.Id,
                Installation(installationId),
                Repository(repositoryId, "openai", name),
                Now,
                TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(
            CompleteAsync(firstRequest, 701, 1001, "one"),
            CompleteAsync(secondRequest, 702, 1002, "two"));

        Assert.Single(results, result => result.Succeeded);
        var rejected = Assert.Single(results, result => !result.Succeeded);
        Assert.Equal("project_repository_limit_reached", rejected.ErrorCode);

        await using var db = await CreateDbContextAsync();
        Assert.Equal(1, await db.ProjectRepositoryLinks.CountAsync(
            item => item.Active,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.RepositoryAccessRequests.CountAsync(
            item => item.Status == GitHubRepositoryAccessRequestStatuses.Completed,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.RepositoryAccessRequests.CountAsync(
            item => item.Status == GitHubRepositoryAccessRequestStatuses.Failed,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Repository_mismatch_failure_leaves_existing_active_connection_unchanged()
    {
        var existingLinkId = await SeedExistingLinkAsync(111111, "existing", "repository");
        var request = await SeedRequestAsync(Guid.Parse("99999999-9999-9999-9999-999999999999"));

        var result = await CreateStore().CompleteAsync(
            request.Id,
            Installation(777),
            Repository(987654, "attacker", "different"),
            Now,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("repository_mismatch", result.ErrorCode);

        await using var db = await CreateDbContextAsync();
        var activeLinks = await db.ProjectRepositoryLinks
            .Where(item => item.Active)
            .ToListAsync(TestContext.Current.CancellationToken);
        var existing = Assert.Single(activeLinks);
        Assert.Equal(existingLinkId, existing.Id);
        Assert.Equal(111111, existing.GitHubRepoId);
        Assert.Equal(1, await db.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Repositories.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Request_expiring_during_installation_binding_is_expired_and_state_consumed_atomically()
    {
        var request = await SeedRequestAsync(Guid.Parse("15151515-1515-1515-1515-151515151515"));
        const string stateHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        await using (var db = await CreateDbContextAsync())
        {
            var persisted = await db.RepositoryAccessRequests.SingleAsync(
                item => item.Id == request.Id,
                TestContext.Current.CancellationToken);
            persisted.ExpiresAt = Now.AddMilliseconds(-1);
            persisted.PendingInstallationId = null;
            db.InstallationFlowStates.Add(new GitHubInstallationFlowState
            {
                Id = Guid.NewGuid(),
                StateHash = stateHash,
                ProjectId = ProjectId,
                InitiatingUserId = UserId,
                FlowType = GitHubInstallationFlowTypes.Requested,
                ReturnPath = "/github/access-updated",
                RepositoryAccessRequestId = request.Id,
                CreatedAt = Now.AddMinutes(-1),
                ExpiresAt = Now.AddMinutes(5)
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var store = new GitHubInstallationStateStore(
            GetRequiredService<IDbContextFactory<GitHubDbContext>>());
        var bound = await store.TryBindRequestedInstallationAsync(
            stateHash,
            request.Id,
            777,
            Now,
            TestContext.Current.CancellationToken);

        Assert.Null(bound);
        await using var verify = await CreateDbContextAsync();
        var persistedRequest = await verify.RepositoryAccessRequests.SingleAsync(
            item => item.Id == request.Id,
            TestContext.Current.CancellationToken);
        var persistedState = await verify.InstallationFlowStates.SingleAsync(
            item => item.StateHash == stateHash,
            TestContext.Current.CancellationToken);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Expired, persistedRequest.Status);
        Assert.Equal(Now, persistedRequest.ConsumedAt);
        Assert.Null(persistedRequest.PendingInstallationId);
        Assert.Equal(Now, persistedState.ConsumedAt);
        Assert.Null(persistedState.PendingInstallationId);
        Assert.Equal(0, await verify.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.ProjectRepositoryLinks.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Expired_request_cannot_create_source_repository_link_or_sync_intent()
    {
        var request = await SeedRequestAsync(Guid.Parse("12121212-1212-1212-1212-121212121212"));
        await using (var db = await CreateDbContextAsync())
        {
            var persisted = await db.RepositoryAccessRequests.SingleAsync(
                item => item.Id == request.Id,
                TestContext.Current.CancellationToken);
            persisted.ExpiresAt = Now.AddSeconds(-1);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateStore().CompleteAsync(
            request.Id,
            Installation(777),
            Repository(987654, "openai", "researchtrack"),
            Now,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubRepositoryAccessFailureCodes.RequestExpired, result.ErrorCode);
        Assert.Null(result.InitialSyncRequest);

        await using var verify = await CreateDbContextAsync();
        var persistedRequest = await verify.RepositoryAccessRequests.SingleAsync(
            item => item.Id == request.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Expired, persistedRequest.Status);
        Assert.NotNull(persistedRequest.ConsumedAt);
        Assert.Equal(0, await verify.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.Repositories.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.ProjectRepositoryLinks.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Failed_request_is_terminal_and_completion_cannot_create_connection()
    {
        var request = await SeedRequestAsync(Guid.Parse("13131313-1313-1313-1313-131313131313"));
        await using (var db = await CreateDbContextAsync())
        {
            var persisted = await db.RepositoryAccessRequests.SingleAsync(
                item => item.Id == request.Id,
                TestContext.Current.CancellationToken);
            persisted.Status = GitHubRepositoryAccessRequestStatuses.Failed;
            persisted.FailureCode = GitHubRepositoryAccessFailureCodes.InvalidInstallation;
            persisted.ConsumedAt = Now.AddSeconds(-1);
            persisted.Version++;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateStore().CompleteAsync(
            request.Id,
            Installation(777),
            Repository(987654, "openai", "researchtrack"),
            Now,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubRepositoryAccessFailureCodes.InvalidInstallation, result.ErrorCode);
        Assert.Null(result.InitialSyncRequest);

        await using var verify = await CreateDbContextAsync();
        Assert.Equal(0, await verify.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.Repositories.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await verify.ProjectRepositoryLinks.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Completed_request_replay_with_different_identity_is_rejected_without_mutation()
    {
        var request = await SeedRequestAsync(Guid.Parse("14141414-1414-1414-1414-141414141414"));
        var store = CreateStore();
        var first = await store.CompleteAsync(
            request.Id,
            Installation(777),
            Repository(987654, "openai", "researchtrack"),
            Now,
            TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded);

        var replay = await store.CompleteAsync(
            request.Id,
            Installation(778),
            Repository(999999, "openai", "researchtrack"),
            Now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.False(replay.Succeeded);
        Assert.Equal(GitHubRepositoryAccessFailureCodes.CompletionInconsistent, replay.ErrorCode);
        Assert.Null(replay.InitialSyncRequest);

        await using var verify = await CreateDbContextAsync();
        Assert.Equal(1, await verify.AccessSources.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.Repositories.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await verify.ProjectRepositoryLinks.CountAsync(TestContext.Current.CancellationToken));
        var persistedRequest = await verify.RepositoryAccessRequests.SingleAsync(
            item => item.Id == request.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Completed, persistedRequest.Status);
        Assert.Equal(987654, persistedRequest.GitHubRepositoryId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private GitHubRepositoryAccessCompletionStore CreateStore(
        int maxLinked = 5,
        int maxEnabled = 5) => new(
        GetRequiredService<IDbContextFactory<GitHubDbContext>>(),
        new GitHubRepositoryLinkOptions(maxLinked, maxEnabled));

    private async Task<GitHubRepositoryAccessRequest> SeedRequestAsync(
        Guid requestId,
        string owner = "openai",
        string name = "researchtrack",
        long installationId = 777,
        char hashCharacter = 'a')
    {
        var request = new GitHubRepositoryAccessRequest
        {
            Id = requestId,
            ProjectId = ProjectId,
            InitiatingUserId = UserId,
            RequestedOwner = owner.ToLowerInvariant(),
            RequestedRepositoryName = name.ToLowerInvariant(),
            RequestedFullName = $"{owner}/{name}".ToLowerInvariant(),
            GitHubRepositoryId = null,
            RequestTokenHash = new string(hashCharacter, 64),
            FlowType = GitHubInstallationFlowTypes.Requested,
            Status = GitHubRepositoryAccessRequestStatuses.Pending,
            CreatedAt = Now.AddMinutes(-2),
            ExpiresAt = Now.AddMinutes(20),
            PendingInstallationId = installationId,
            AuthorizationStartedAt = Now.AddMinutes(-1),
            Version = 1
        };

        await using var db = await CreateDbContextAsync();
        db.RepositoryAccessRequests.Add(request);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return request;
    }

    private async Task<Guid> SeedExistingLinkAsync(long gitHubRepositoryId, string owner, string name)
    {
        var sourceId = Guid.NewGuid();
        var repositoryId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        await using var db = await CreateDbContextAsync();
        db.AccessSources.Add(new GitHubAccessSource
        {
            Id = sourceId,
            ProjectId = ProjectId,
            CreatedByUserId = UserId,
            InstallationId = null,
            OwnerLogin = owner,
            OwnerType = "PUBLIC",
            AccessType = GitHubAccessTypes.PublicUrl,
            Active = true,
            ActiveRepositoryKey = $"{ProjectId:N}:{GitHubAccessTypes.PublicUrl}:{gitHubRepositoryId}",
            ActiveInstallationKey = null,
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1)
        });
        db.Repositories.Add(new GitHubRepository
        {
            Id = repositoryId,
            SourceId = sourceId,
            GitHubRepositoryId = gitHubRepositoryId,
            FullName = $"{owner}/{name}",
            Name = name,
            OwnerLogin = owner,
            DefaultBranch = "main",
            Url = $"https://github.com/{owner}/{name}",
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1)
        });
        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            Id = linkId,
            ProjectId = ProjectId,
            SourceId = sourceId,
            GitHubRepositoryId = repositoryId,
            GitHubRepoId = gitHubRepositoryId,
            LinkedByUserId = UserId,
            AccessType = GitHubAccessTypes.PublicUrl,
            FullName = $"{owner}/{name}",
            Name = name,
            CustomName = null,
            OwnerLogin = owner,
            DefaultBranch = "main",
            Url = $"https://github.com/{owner}/{name}",
            Active = true,
            Primary = true,
            Enabled = true,
            ActiveRepositoryKey = $"{ProjectId:N}:{gitHubRepositoryId}",
            PrimaryProjectKey = ProjectId.ToString("N"),
            LinkedAt = Now.AddDays(-1),
            LastSyncedAt = Now.AddHours(-1),
            SyncStatus = GitHubSyncStatuses.Success,
            UpdatedAt = Now.AddDays(-1)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return linkId;
    }

    private static GitHubAccessSource InstallationSource(Guid sourceId, long installationId) => new()
    {
        Id = sourceId,
        ProjectId = ProjectId,
        CreatedByUserId = UserId,
        InstallationId = installationId,
        OwnerLogin = "openai",
        OwnerType = "ORG",
        AccessType = GitHubAccessTypes.InstallationDirect,
        Active = true,
        // Simulate a legacy/conflicting row that predates the canonical installation uniqueness key.
        ActiveInstallationKey = null,
        ActiveRepositoryKey = null,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    private static GitHubInstallationInfo Installation(long installationId) =>
        new(installationId, "openai", "ORG");

    private static GitHubInstallationRepository Repository(
        long repositoryId,
        string owner,
        string name) => new(
        repositoryId,
        owner,
        name,
        $"{owner}/{name}",
        $"https://github.com/{owner}/{name}",
        "main",
        true);

    private T GetRequiredService<T>() where T : notnull =>
        (_factory ?? throw new InvalidOperationException("Factory is unavailable."))
        .Services.GetRequiredService<T>();

    private async Task<GitHubDbContext> CreateDbContextAsync() =>
        await GetRequiredService<IDbContextFactory<GitHubDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
}
