using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Features.Installation;

public sealed class GitHubRepositoryAccessTokenServiceTests
{
    private static readonly Guid RequestId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
    private const string RawToken = "owner-grant-secret-token-value";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Malformed_tokens_are_rejected_with_same_generic_response(string? token)
    {
        var service = CreateService(new StubStore());

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ValidateAsync(
            token,
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("Owner-granted GitHub request is unavailable.", exception.Message);
    }

    [Fact]
    public async Task Unknown_token_is_rejected_without_disclosing_project_context()
    {
        var store = new StubStore { Request = PendingRequest() };
        var service = CreateService(store);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ValidateAsync(
            "different-unknown-token",
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("Owner-granted GitHub request is unavailable.", exception.Message);
        Assert.DoesNotContain(ProjectId.ToString("D"), exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("openai/researchtrack", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expired_pending_token_is_materialized_and_reported_as_expired()
    {
        var request = PendingRequest();
        request.ExpiresAt = Now.AddSeconds(-1).UtcDateTime;
        var store = new StubStore { Request = request };
        var service = CreateService(store);

        var response = await service.ValidateAsync(RawToken, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Expired, response.Status);
        Assert.True(store.ExpireWasCalled);
        Assert.Null(response.FailureCode);
        Assert.Equal("openai/researchtrack", response.RepositoryFullName);
    }

    [Theory]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Completed)]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Failed)]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Expired)]
    public async Task Terminal_token_cannot_continue(string terminalStatus)
    {
        var request = PendingRequest();
        request.Status = terminalStatus;
        request.FailureCode = terminalStatus == GitHubRepositoryAccessRequestStatuses.Failed
            ? GitHubRepositoryAccessFailureCodes.InvalidInstallation
            : null;
        var store = new StubStore { Request = request };
        var service = CreateService(store);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.RequirePendingAsync(
            RawToken,
            TestContext.Current.CancellationToken));

        Assert.True(exception.StatusCode is StatusCodes.Status409Conflict or StatusCodes.Status410Gone);
        Assert.False(store.BindWasCalled);
    }

    [Fact]
    public async Task Validation_never_returns_hash_or_raw_token()
    {
        var store = new StubStore { Request = PendingRequest() };
        var service = CreateService(store);

        var response = await service.ValidateAsync(RawToken, TestContext.Current.CancellationToken);
        var rendered = System.Text.Json.JsonSerializer.Serialize(response);

        Assert.DoesNotContain(RawToken, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(store.Request!.RequestTokenHash, rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Database_expiry_serializes_as_utc_for_non_utc_owner_browsers()
    {
        var request = PendingRequest();
        request.ExpiresAt = DateTime.SpecifyKind(request.ExpiresAt, DateTimeKind.Unspecified);
        var response = await CreateService(new StubStore { Request = request })
            .ValidateAsync(RawToken, TestContext.Current.CancellationToken);
        Assert.Equal(DateTimeKind.Utc, response.ExpiresAt.Kind);
        Assert.Contains("2026-09-14T09:10:00Z", System.Text.Json.JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    private static GitHubRepositoryAccessTokenService CreateService(StubStore store) => new(
        store,
        new FixedTimeProvider(Now),
        NullLogger<GitHubRepositoryAccessTokenService>.Instance);

    private static GitHubRepositoryAccessRequest PendingRequest() => new()
    {
        Id = RequestId,
        ProjectId = ProjectId,
        InitiatingUserId = UserId,
        RequestedOwner = "openai",
        RequestedRepositoryName = "researchtrack",
        RequestedFullName = "openai/researchtrack",
        RequestTokenHash = HashToken(RawToken),
        FlowType = GitHubInstallationFlowTypes.Requested,
        Status = GitHubRepositoryAccessRequestStatuses.Pending,
        CreatedAt = Now.AddMinutes(-1).UtcDateTime,
        ExpiresAt = Now.AddMinutes(10).UtcDateTime,
        Version = 0
    };


    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubStore : IGitHubRepositoryAccessRequestStore
    {
        public GitHubRepositoryAccessRequest? Request { get; set; }
        public bool ExpireWasCalled { get; private set; }
        public bool BindWasCalled { get; private set; }

        public Task CreateAsync(GitHubRepositoryAccessRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.CompletedTask;
        }

        public Task<GitHubRepositoryAccessRequest?> FindByIdAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Request?.Id == requestId ? Request : null);

        public Task<GitHubRepositoryAccessRequest?> FindByTokenHashAsync(string requestTokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Request?.RequestTokenHash == requestTokenHash ? Request : null);

        public Task<OwnerGrantProjectLinkState> GetProjectLinkStateAsync(
            Guid projectId,
            string normalizedFullName,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OwnerGrantProjectLinkState(0, 0, false));

        public Task<GitHubRepositoryAccessRequest?> TryFailAsync(
            Guid requestId,
            string failureCode,
            DateTime now,
            CancellationToken cancellationToken) => Task.FromResult(Request);

        public Task<GitHubRepositoryAccessRequest?> TryExpireAsync(
            Guid requestId,
            DateTime now,
            CancellationToken cancellationToken)
        {
            ExpireWasCalled = true;
            if (Request is not null
                && Request.Id == requestId
                && Request.Status == GitHubRepositoryAccessRequestStatuses.Pending
                && Request.ExpiresAt <= now)
            {
                Request.Status = GitHubRepositoryAccessRequestStatuses.Expired;
                Request.ConsumedAt = now;
                Request.Version++;
                return Task.FromResult<GitHubRepositoryAccessRequest?>(Request);
            }

            return Task.FromResult<GitHubRepositoryAccessRequest?>(null);
        }
    }
}
