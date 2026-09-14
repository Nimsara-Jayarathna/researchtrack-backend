using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Features.Installation;

public sealed class GitHubRepositoryAccessRequestServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_persists_pending_exact_repository_with_hash_only()
    {
        var store = new StubStore();
        var authorization = new StubAuthorization();
        var service = CreateService(store, authorization);

        var response = await service.CreateAsync(
            UserId,
            new CreateGitHubRepositoryAccessRequest(
                ProjectId,
                "https://github.com/OpenAI/ResearchTrack.git"),
            TestContext.Current.CancellationToken);

        Assert.True(authorization.WasCalled);
        Assert.NotNull(store.Created);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Pending, store.Created.Status);
        Assert.Equal("openai", store.Created.RequestedOwner);
        Assert.Equal("researchtrack", store.Created.RequestedRepositoryName);
        Assert.Equal("openai/researchtrack", store.Created.RequestedFullName);
        Assert.Equal(64, store.Created.RequestTokenHash.Length);
        Assert.DoesNotContain(store.Created.RequestTokenHash, response.RequestUrl, StringComparison.OrdinalIgnoreCase);
        var rawToken = Uri.UnescapeDataString(new Uri(response.RequestUrl).Query.Split("token=", 2, StringSplitOptions.None)[1]);
        Assert.Equal(32, WebEncoders.Base64UrlDecode(rawToken).Length);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        Assert.Equal(expectedHash, store.Created.RequestTokenHash);
        Assert.Equal(Now.AddMinutes(10).UtcDateTime, response.ExpiresAt);
        Assert.Contains("https://app.example.test/github/request-access?token=", response.RequestUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_audit_log_does_not_include_returned_raw_bearer_token()
    {
        var store = new StubStore();
        var logger = new CaptureLogger<GitHubRepositoryAccessRequestService>();
        var service = CreateService(store, new StubAuthorization(), logger);

        var response = await service.CreateAsync(
            UserId,
            new CreateGitHubRepositoryAccessRequest(ProjectId, "https://github.com/openai/researchtrack"),
            TestContext.Current.CancellationToken);

        var rawToken = new Uri(response.RequestUrl).Query.Split("token=", 2, StringSplitOptions.None)[1];
        var rendered = string.Join("\n", logger.Messages);
        Assert.DoesNotContain(Uri.UnescapeDataString(rawToken), rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(store.Created!.RequestTokenHash, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(response.RequestId.ToString("D"), rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_rejects_empty_project_id_without_persisting_request()
    {
        var store = new StubStore();
        var authorization = new StubAuthorization();
        var service = CreateService(store, authorization);

        await Assert.ThrowsAsync<ApiValidationException>(() => service.CreateAsync(
            UserId,
            new CreateGitHubRepositoryAccessRequest(Guid.Empty, "https://github.com/openai/researchtrack"),
            TestContext.Current.CancellationToken));

        Assert.False(authorization.WasCalled);
        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Create_rejects_invalid_repository_url_without_persisting_request()
    {
        var store = new StubStore();
        var service = CreateService(store, new StubAuthorization());

        await Assert.ThrowsAsync<ApiValidationException>(() => service.CreateAsync(
            UserId,
            new CreateGitHubRepositoryAccessRequest(ProjectId, "https://example.com/not-github/repo"),
            TestContext.Current.CancellationToken));

        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Create_propagates_project_authorization_failure_without_persisting_request()
    {
        var store = new StubStore();
        var authorization = new StubAuthorization
        {
            Failure = new ApiException(StatusCodes.Status403Forbidden, "forbidden", "Forbidden")
        };
        var service = CreateService(store, authorization);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            UserId,
            new CreateGitHubRepositoryAccessRequest(ProjectId, "https://github.com/openai/researchtrack"),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Create_rejects_existing_exact_repository_before_request_persistence()
    {
        var store = new StubStore
        {
            LinkState = new OwnerGrantProjectLinkState(1, 1, true)
        };
        var service = CreateService(store, new StubAuthorization());

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateAsync(
            UserId,
            new CreateGitHubRepositoryAccessRequest(ProjectId, "https://github.com/openai/researchtrack"),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Null(store.Created);
    }

    [Fact]
    public async Task Member_status_materializes_expired_without_changing_repository_connection()
    {
        var request = PendingRequest();
        request.ExpiresAt = Now.AddSeconds(-1).UtcDateTime;
        var store = new StubStore { Existing = request };
        var service = CreateService(store, new StubAuthorization());

        var response = await service.GetStatusAsync(
            UserId,
            request.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Expired, response.Status);
        Assert.True(store.ExpireWasCalled);
        Assert.Equal(0, store.ConnectionMutationCount);
    }

    [Fact]
    public async Task Member_status_does_not_disclose_request_to_different_project_user()
    {
        var request = PendingRequest();
        var store = new StubStore { Existing = request };
        var service = CreateService(store, new StubAuthorization());

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.GetStatusAsync(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            request.Id,
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.DoesNotContain(request.RequestedFullName, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubRepositoryAccessRequestService CreateService(
        StubStore store,
        StubAuthorization authorization,
        ILogger<GitHubRepositoryAccessRequestService>? logger = null) => new(
        authorization,
        store,
        new GitHubRepositoryLinkOptions(5, 5),
        new GitHubAppOptions(
            12345,
            "researchtrack-test",
            "Iv1.test-client",
            "test-client-secret",
            "/tmp/test.pem",
            new Uri("https://api.example.test/api/github/access-source/install/callback"),
            new Uri("https://app.example.test/"),
            TimeSpan.FromMinutes(10)),
        new FixedTimeProvider(Now),
        logger ?? NullLogger<GitHubRepositoryAccessRequestService>.Instance);

    private static GitHubRepositoryAccessRequest PendingRequest() => new()
    {
        Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        ProjectId = ProjectId,
        InitiatingUserId = UserId,
        RequestedOwner = "openai",
        RequestedRepositoryName = "researchtrack",
        RequestedFullName = "openai/researchtrack",
        RequestTokenHash = new string('a', 64),
        FlowType = GitHubInstallationFlowTypes.Requested,
        Status = GitHubRepositoryAccessRequestStatuses.Pending,
        CreatedAt = Now.AddMinutes(-1).UtcDateTime,
        ExpiresAt = Now.AddMinutes(10).UtcDateTime,
        Version = 0
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubAuthorization : IProjectAuthorizationClient
    {
        public bool WasCalled { get; private set; }
        public Exception? Failure { get; init; }

        public Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            Assert.Equal(ProjectId, projectId);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class StubStore : IGitHubRepositoryAccessRequestStore
    {
        public GitHubRepositoryAccessRequest? Created { get; private set; }
        public GitHubRepositoryAccessRequest? Existing { get; set; }
        public OwnerGrantProjectLinkState LinkState { get; set; } = new(0, 0, false);
        public bool ExpireWasCalled { get; private set; }
        public int ConnectionMutationCount { get; private set; }

        public Task CreateAsync(GitHubRepositoryAccessRequest request, CancellationToken cancellationToken)
        {
            Created = request;
            Existing = request;
            return Task.CompletedTask;
        }

        public Task<GitHubRepositoryAccessRequest?> FindByIdAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Existing?.Id == requestId ? Existing : null);

        public Task<GitHubRepositoryAccessRequest?> FindByTokenHashAsync(string requestTokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Existing?.RequestTokenHash == requestTokenHash ? Existing : null);

        public Task<OwnerGrantProjectLinkState> GetProjectLinkStateAsync(
            Guid projectId,
            string normalizedFullName,
            CancellationToken cancellationToken)
        {
            Assert.Equal(ProjectId, projectId);
            Assert.Equal("openai/researchtrack", normalizedFullName);
            return Task.FromResult(LinkState);
        }

        public Task<GitHubRepositoryAccessRequest?> TryFailAsync(Guid requestId, string failureCode, DateTime now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Status/create must not fail requests.");

        public Task<GitHubRepositoryAccessRequest?> TryExpireAsync(Guid requestId, DateTime now, CancellationToken cancellationToken)
        {
            ExpireWasCalled = true;
            if (Existing is not null
                && Existing.Id == requestId
                && Existing.Status == GitHubRepositoryAccessRequestStatuses.Pending
                && Existing.ExpiresAt <= now)
            {
                Existing.Status = GitHubRepositoryAccessRequestStatuses.Expired;
                Existing.ConsumedAt = now;
                Existing.Version++;
                return Task.FromResult<GitHubRepositoryAccessRequest?>(Existing);
            }

            return Task.FromResult<GitHubRepositoryAccessRequest?>(null);
        }
    }
}
