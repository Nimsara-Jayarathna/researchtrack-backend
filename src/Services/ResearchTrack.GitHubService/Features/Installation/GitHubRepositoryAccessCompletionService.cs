using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubRepositoryAccessCompletionService : IGitHubRepositoryAccessCompletionService
{
    private readonly IGitHubRepositoryAccessCompletionStore _store;
    private readonly IInitialRepositorySyncRequester _syncRequester;
    private readonly IRepositoryLinkStore _repositoryLinkStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubRepositoryAccessCompletionService> _logger;

    public GitHubRepositoryAccessCompletionService(
        IGitHubRepositoryAccessCompletionStore store,
        IInitialRepositorySyncRequester syncRequester,
        IRepositoryLinkStore repositoryLinkStore,
        TimeProvider timeProvider,
        ILogger<GitHubRepositoryAccessCompletionService> logger)
    {
        _store = store;
        _syncRequester = syncRequester;
        _repositoryLinkStore = repositoryLinkStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitHubRepositoryAccessCompletionResult> CompleteAsync(
        Guid requestId,
        GitHubInstallationInfo installation,
        GitHubInstallationRepository repository,
        CancellationToken cancellationToken)
    {
        var persisted = await _store.CompleteAsync(
            requestId,
            installation,
            repository,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        var handoffSucceeded = persisted.Succeeded;
        if (persisted.Succeeded
            && !persisted.AlreadyCompleted
            && persisted.InitialSyncRequest is not null)
        {
            try
            {
                await _syncRequester.RequestAsync(
                    persisted.InitialSyncRequest,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                handoffSucceeded = false;
                _logger.LogError(
                    exception,
                    "Initial owner-granted repository synchronization handoff failed. RequestId={RequestId} LinkedRepositoryId={LinkedRepositoryId}",
                    requestId,
                    persisted.InitialSyncRequest.LinkedRepositoryId);

                try
                {
                    await _repositoryLinkStore.MarkSyncFailedAsync(
                        persisted.InitialSyncRequest.LinkedRepositoryId,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        CancellationToken.None);
                }
                catch (Exception statusException)
                {
                    _logger.LogError(
                        statusException,
                        "Unable to mark failed owner-granted synchronization handoff. RequestId={RequestId} LinkedRepositoryId={LinkedRepositoryId}",
                        requestId,
                        persisted.InitialSyncRequest.LinkedRepositoryId);
                }
            }
        }

        return new GitHubRepositoryAccessCompletionResult(
            persisted.RequestId,
            persisted.ProjectId,
            persisted.Succeeded,
            persisted.AlreadyCompleted,
            persisted.ErrorCode,
            persisted.SourceId,
            persisted.RepositoryId,
            persisted.LinkedRepositoryId,
            persisted.GitHubRepositoryId,
            handoffSucceeded);
    }
}
