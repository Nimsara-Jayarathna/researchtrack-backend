using System.Security.Cryptography;
using System.Text;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubInstallationStateService : IGitHubInstallationStateService
{
    private const int StateBytes = 32;
    private readonly IGitHubInstallationStateStore _store;
    private readonly GitHubAppOptions _options;
    private readonly TimeProvider _timeProvider;

    public GitHubInstallationStateService(
        IGitHubInstallationStateStore store,
        GitHubAppOptions options,
        TimeProvider timeProvider)
    {
        _store = store;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<GitHubInstallationState> CreateAsync(
        Guid projectId,
        Guid initiatingUserId,
        string flowType,
        string returnPath,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || initiatingUserId == Guid.Empty)
        {
            throw new ArgumentException("Project and initiating user are required.");
        }

        if (!string.Equals(flowType, GitHubInstallationFlowTypes.Direct, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Requested installation state must be created with a bound owner-grant request.",
                nameof(flowType));
        }
        ValidateReturnPath(returnPath);

        var value = Base64UrlEncode(RandomNumberGenerator.GetBytes(StateBytes));
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(_options.StateLifetime);
        await _store.CreateAsync(
            new GitHubInstallationFlowState
            {
                Id = Guid.NewGuid(),
                StateHash = HashState(value),
                ProjectId = projectId,
                InitiatingUserId = initiatingUserId,
                FlowType = flowType,
                ReturnPath = returnPath,
                CreatedAt = now,
                ExpiresAt = expiresAt
            },
            cancellationToken);

        return new GitHubInstallationState(value, expiresAt);
    }

    public async Task<GitHubInstallationState> CreateRequestedAsync(
        Guid projectId,
        Guid initiatingUserId,
        Guid repositoryAccessRequestId,
        string returnPath,
        CancellationToken cancellationToken)
    {
        if (repositoryAccessRequestId == Guid.Empty)
        {
            throw new ArgumentException("Repository access request id is required.", nameof(repositoryAccessRequestId));
        }

        if (projectId == Guid.Empty || initiatingUserId == Guid.Empty)
        {
            throw new ArgumentException("Project and initiating user are required.");
        }

        ValidateReturnPath(returnPath);
        var value = Base64UrlEncode(RandomNumberGenerator.GetBytes(StateBytes));
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.Add(_options.StateLifetime);
        var outcome = await _store.CreateRequestedAsync(
            new GitHubInstallationFlowState
            {
                Id = Guid.NewGuid(),
                StateHash = HashState(value),
                ProjectId = projectId,
                InitiatingUserId = initiatingUserId,
                FlowType = GitHubInstallationFlowTypes.Requested,
                ReturnPath = returnPath,
                RepositoryAccessRequestId = repositoryAccessRequestId,
                CreatedAt = now,
                ExpiresAt = expiresAt
            },
            now,
            cancellationToken);

        return outcome switch
        {
            RequestedInstallationStateCreateOutcome.Created => new GitHubInstallationState(value, expiresAt),
            RequestedInstallationStateCreateOutcome.Expired => throw new ApiException(
                StatusCodes.Status410Gone,
                ErrorCodes.Conflict,
                "Owner-granted GitHub request has expired."),
            RequestedInstallationStateCreateOutcome.NotPending => throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Owner-granted GitHub request is no longer pending."),
            _ => throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "Owner-granted GitHub request is unavailable.")
        };
    }

    public async Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(
        string state,
        CancellationToken cancellationToken)
    {
        var existing = await GetValidExternalStateAsync(state, expectedFlowType: null, cancellationToken);
        return MapValidated(existing);
    }

    public async Task<ValidatedGitHubInstallationState> ValidateAsync(
        string state,
        Guid expectedUserId,
        string expectedFlowType,
        CancellationToken cancellationToken)
    {
        var existing = await GetValidStateAsync(
            state, expectedUserId, expectedFlowType, cancellationToken);
        return MapValidated(existing);
    }

    public async Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(
        string state,
        string expectedFlowType,
        CancellationToken cancellationToken)
    {
        var existing = await GetValidExternalStateAsync(
            state, expectedFlowType, cancellationToken);
        return MapValidated(existing);
    }

    public async Task<ValidatedGitHubInstallationState> BindInstallationAsync(
        string state,
        long installationId,
        string expectedFlowType,
        CancellationToken cancellationToken)
    {
        if (installationId <= 0)
        {
            throw InvalidState("GitHub installation id is invalid.");
        }

        var existing = await GetValidExternalStateAsync(
            state, expectedFlowType, cancellationToken);
        if (existing.PendingInstallationId is not null
            && existing.PendingInstallationId != installationId)
        {
            throw InvalidState(
                "GitHub installation state is already bound to a different installation.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var bound = await _store.TryBindInstallationAsync(
            HashState(state),
            installationId,
            expectedFlowType,
            now,
            cancellationToken);
        if (bound is null)
        {
            throw InvalidState("GitHub installation state could not be bound to the installation.");
        }

        return MapValidated(bound);
    }

    public async Task<ValidatedGitHubInstallationState> BindRequestedInstallationAsync(
        string state,
        Guid repositoryAccessRequestId,
        long installationId,
        CancellationToken cancellationToken)
    {
        if (repositoryAccessRequestId == Guid.Empty || installationId <= 0)
        {
            throw InvalidState("Requested GitHub installation binding is invalid.");
        }

        var existing = await GetValidExternalStateAsync(
            state, GitHubInstallationFlowTypes.Requested, cancellationToken);
        if (existing.RepositoryAccessRequestId != repositoryAccessRequestId)
        {
            throw InvalidState("GitHub installation state does not match the owner-granted request.");
        }

        var bound = await _store.TryBindRequestedInstallationAsync(
            HashState(state),
            repositoryAccessRequestId,
            installationId,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        if (bound is null)
        {
            throw InvalidState("Owner-granted GitHub installation could not be bound safely.");
        }

        return MapValidated(bound);
    }

    public async Task<ConsumedGitHubInstallationState> ConsumeAsync(
        string state,
        Guid expectedUserId,
        string expectedFlowType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw InvalidState("GitHub installation state is required.");
        }

        ValidateFlowType(expectedFlowType);
        var stateHash = HashState(state);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var consumed = await _store.TryConsumeAsync(
            stateHash,
            expectedUserId,
            expectedFlowType,
            now,
            cancellationToken);
        if (consumed is not null)
        {
            return consumed;
        }

        await GetValidStateAsync(state, expectedUserId, expectedFlowType, cancellationToken);
        throw InvalidState("GitHub installation state could not be consumed.");
    }

    private async Task<GitHubInstallationFlowState> GetValidExternalStateAsync(
        string state,
        string? expectedFlowType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw InvalidState("GitHub installation state is required.");
        }

        if (expectedFlowType is not null)
        {
            ValidateFlowType(expectedFlowType);
        }
        var stateHash = HashState(state);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = await _store.FindAsync(stateHash, cancellationToken);
        if (existing is null)
        {
            throw InvalidState("GitHub installation state is unknown or invalid.");
        }

        if (existing.ConsumedAt is not null)
        {
            throw InvalidState("GitHub installation state has already been used.");
        }

        if (existing.ExpiresAt <= now)
        {
            throw InvalidState("GitHub installation state has expired.");
        }

        if (expectedFlowType is not null
            && !string.Equals(existing.FlowType, expectedFlowType, StringComparison.Ordinal))
        {
            throw InvalidState("GitHub installation state does not match the expected ResearchTrack flow.");
        }

        return existing;
    }

    private async Task<GitHubInstallationFlowState> GetValidStateAsync(
        string state,
        Guid expectedUserId,
        string expectedFlowType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw InvalidState("GitHub installation state is required.");
        }

        ValidateFlowType(expectedFlowType);
        var stateHash = HashState(state);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = await _store.FindAsync(stateHash, cancellationToken);
        if (existing is null)
        {
            throw InvalidState("GitHub installation state is unknown or invalid.");
        }

        if (existing.ConsumedAt is not null)
        {
            throw InvalidState("GitHub installation state has already been used.");
        }

        if (existing.ExpiresAt <= now)
        {
            throw InvalidState("GitHub installation state has expired.");
        }

        if (existing.InitiatingUserId != expectedUserId
            || !string.Equals(existing.FlowType, expectedFlowType, StringComparison.Ordinal))
        {
            throw InvalidState("GitHub installation state does not match the expected ResearchTrack context.");
        }

        return existing;
    }

    private static ValidatedGitHubInstallationState MapValidated(
        GitHubInstallationFlowState state) => new(
            state.ProjectId,
            state.InitiatingUserId,
            state.FlowType,
            state.ReturnPath,
            state.RepositoryAccessRequestId,
            state.PendingInstallationId,
            state.AuthorizationStartedAt);

    internal static string HashState(string state)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(state));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static void ValidateFlowType(string flowType)
    {
        if (!string.Equals(flowType, GitHubInstallationFlowTypes.Direct, StringComparison.Ordinal)
            && !string.Equals(flowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsupported GitHub installation flow type.", nameof(flowType));
        }
    }

    private static void ValidateReturnPath(string returnPath)
    {
        if (string.IsNullOrWhiteSpace(returnPath)
            || !returnPath.StartsWith("/", StringComparison.Ordinal)
            || returnPath.StartsWith("//", StringComparison.Ordinal)
            || returnPath.StartsWith("/\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("Return context must be a local ResearchTrack path.", nameof(returnPath));
        }
    }

    private static ApiException InvalidState(string message) => new(
        StatusCodes.Status400BadRequest,
        ErrorCodes.ValidationError,
        message);
}
