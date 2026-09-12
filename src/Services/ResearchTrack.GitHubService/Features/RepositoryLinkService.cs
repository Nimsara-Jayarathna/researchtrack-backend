using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Features;

public sealed class RepositoryLinkService : IRepositoryLinkService
{
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IRepositoryLinkStore _store;
    private readonly IGitHubInstallationRepositoryService _installationRepositoryService;
    private readonly IInitialRepositorySyncRequester _syncRequester;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RepositoryLinkService> _logger;

    public RepositoryLinkService(
        IProjectAuthorizationClient projectAuthorization,
        IRepositoryLinkStore store,
        IGitHubInstallationRepositoryService installationRepositoryService,
        IInitialRepositorySyncRequester syncRequester,
        TimeProvider timeProvider,
        ILogger<RepositoryLinkService> logger)
    {
        _projectAuthorization = projectAuthorization;
        _store = store;
        _installationRepositoryService = installationRepositoryService;
        _syncRequester = syncRequester;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ProjectGitHubRepositoriesResponse> LinkAsync(
        Guid userId,
        LinkGitHubRepositoriesRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await _projectAuthorization.EnsureCanManageAsync(request.ProjectId, cancellationToken);

        // Public URL sources retain their existing validation path. Installation-backed
        // sources are re-verified against GitHub immediately before persistence.
        await _installationRepositoryService.TryVerifyForLinkAsync(
            userId,
            request.ProjectId,
            request.SourceId,
            request.Repositories!,
            cancellationToken);

        var persisted = await _store.CreateLinksAsync(
            request.ProjectId,
            request.SourceId,
            userId,
            request.Repositories!,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        var handoffFailed = false;
        foreach (var syncRequest in persisted.SyncRequests)
        {
            try
            {
                await _syncRequester.RequestAsync(syncRequest, CancellationToken.None);
            }
            catch (Exception exception)
            {
                handoffFailed = true;
                _logger.LogError(
                    exception,
                    "Initial repository synchronization handoff failed. LinkedRepositoryId={LinkedRepositoryId}",
                    syncRequest.LinkedRepositoryId);

                try
                {
                    await _store.MarkSyncFailedAsync(
                        syncRequest.LinkedRepositoryId,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        CancellationToken.None);
                }
                catch (Exception statusException)
                {
                    _logger.LogError(
                        statusException,
                        "Unable to mark failed synchronization handoff. LinkedRepositoryId={LinkedRepositoryId}",
                        syncRequest.LinkedRepositoryId);
                }
            }
        }

        return handoffFailed
            ? await _store.GetProjectAsync(request.ProjectId, CancellationToken.None)
            : persisted.Response;
    }

    public async Task<ProjectGitHubRepositoriesResponse> GetProjectAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw new ApiValidationException([
                new ApiFieldError("projectId", ["Project id is required."])
            ]);
        }

        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        return await _store.GetProjectAsync(projectId, cancellationToken);
    }

    private static void Validate(LinkGitHubRepositoriesRequest request)
    {
        var errors = new List<ApiFieldError>();
        if (request.ProjectId == Guid.Empty)
        {
            errors.Add(new ApiFieldError("projectId", ["Project id is required."]));
        }
        if (request.SourceId == Guid.Empty)
        {
            errors.Add(new ApiFieldError("sourceId", ["Access source id is required."]));
        }
        if (request.Repositories is null || request.Repositories.Count == 0)
        {
            errors.Add(new ApiFieldError("repositories", ["Select at least one repository."]));
        }
        else
        {
            if (request.Repositories.Any(repository => repository.GitHubRepositoryId == Guid.Empty))
            {
                errors.Add(new ApiFieldError(
                    "repositories.githubRepositoryId",
                    ["Repository id is required."]));
            }
            if (request.Repositories
                .GroupBy(repository => repository.GitHubRepositoryId)
                .Any(group => group.Count() > 1))
            {
                errors.Add(new ApiFieldError(
                    "repositories",
                    ["The same repository cannot be selected more than once."]));
            }
            if (request.Repositories.Count(repository => repository.Primary == true) > 1)
            {
                errors.Add(new ApiFieldError(
                    "repositories.primary",
                    ["Only one repository can be selected as primary."]));
            }
            if (request.Repositories.Any(repository => repository.CustomName?.Trim().Length > 255))
            {
                errors.Add(new ApiFieldError(
                    "repositories.customName",
                    ["Display name must be 255 characters or less."]));
            }
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }
    }
}
