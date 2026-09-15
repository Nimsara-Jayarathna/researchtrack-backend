using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubInstallationRepositoryService : IGitHubInstallationRepositoryService
{
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IInstallationRepositoryStore _store;
    private readonly IGitHubInstallationRepositoryInventoryService _inventoryService;
    private readonly IGitHubAppClient _gitHubAppClient;
    private readonly IGitHubInstallationRepositoryClient _repositoryClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubInstallationRepositoryService> _logger;

    public GitHubInstallationRepositoryService(
        IProjectAuthorizationClient projectAuthorization,
        IInstallationRepositoryStore store,
        IGitHubInstallationRepositoryInventoryService inventoryService,
        IGitHubAppClient gitHubAppClient,
        IGitHubInstallationRepositoryClient repositoryClient,
        TimeProvider timeProvider,
        ILogger<GitHubInstallationRepositoryService> logger)
    {
        _projectAuthorization = projectAuthorization;
        _store = store;
        _inventoryService = inventoryService;
        _gitHubAppClient = gitHubAppClient;
        _repositoryClient = repositoryClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitHubAvailableRepositoriesResponse?> TryGetAvailableAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ApiValidationException([
                new ApiFieldError("sourceId", ["Access source id is required."])
            ]);
        }

        var source = await _store.GetSourceAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        await _projectAuthorization.EnsureCanManageAsync(source.ProjectId, cancellationToken);

        var response = await _inventoryService.TryRefreshAsync(source.Id, cancellationToken)
            ?? throw NotFound("The GitHub App installation source is no longer active.");

        _logger.LogInformation(
            "Listed repositories accessible to GitHub App installation. ProjectId={ProjectId} SourceId={SourceId} InstallationId={InstallationId} RepositoryCount={RepositoryCount} UserId={UserId}",
            source.ProjectId,
            source.Id,
            source.InstallationId,
            response.TotalCount,
            userId);

        return response;
    }

    public async Task<GitHubInstallationRepositoriesPageResponse> GetInstallationPageAsync(
        Guid userId,
        Guid projectId,
        long installationId,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw new ApiValidationException([
                new ApiFieldError("projectId", ["Project id is required."])
            ]);
        }
        if (installationId <= 0)
        {
            throw new ApiValidationException([
                new ApiFieldError("installationId", ["Installation id must be positive."])
            ]);
        }
        if (page < 1)
        {
            throw new ApiValidationException([
                new ApiFieldError("page", ["Page must be at least 1."])
            ]);
        }
        if (size is < 1 or > 100)
        {
            throw new ApiValidationException([
                new ApiFieldError("size", ["Page size must be between 1 and 100."])
            ]);
        }

        var source = await _store.GetSourceByInstallationAsync(
            projectId,
            installationId,
            cancellationToken)
            ?? throw NotFound("The GitHub App installation source was not found for this project.");

        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var available = await TryGetAvailableAsync(userId, source.Id, cancellationToken)
            ?? throw NotFound("The GitHub App installation source is no longer active.");

        var skip = (long)(page - 1) * size;
        var items = skip >= available.TotalCount
            ? new List<GitHubInstallationRepositoryResponse>()
            : available.Items
                .Skip((int)skip)
                .Take(size)
                .Select(item => new GitHubInstallationRepositoryResponse(
                    item.GitHubRepoId,
                    item.Name,
                    item.FullName,
                    item.Url,
                    item.OwnerLogin,
                    item.DefaultBranch))
                .ToList();
        var hasNext = skip + items.Count < available.TotalCount;

        return new GitHubInstallationRepositoriesPageResponse(
            items,
            page,
            size,
            items.Count,
            available.TotalCount,
            hasNext,
            page > 1,
            hasNext ? page + 1 : null);
    }

    public async Task<LegacyInstallationRepositorySelection> ResolveLegacySelectionAsync(
        Guid userId,
        Guid projectId,
        long installationId,
        long repositoryId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || installationId <= 0 || repositoryId <= 0)
        {
            throw new ApiValidationException([
                new ApiFieldError(
                    "repository",
                    ["Project id, installation id, and repository id are required."])
            ]);
        }

        var source = await _store.GetSourceByInstallationAsync(
            projectId,
            installationId,
            cancellationToken)
            ?? throw NotFound("The GitHub App installation source was not found for this project.");

        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);

        var token = await _gitHubAppClient.CreateInstallationTokenAsync(
            source.InstallationId,
            cancellationToken);
        var authoritative = await GetAccessibleRepositoryAsync(
            token,
            repositoryId,
            cancellationToken);
        var storedRepositoryId = await _store.UpsertVerifiedAsync(
            source.Id,
            authoritative,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        _logger.LogInformation(
            "Resolved legacy GitHub installation repository selection. ProjectId={ProjectId} SourceId={SourceId} InstallationId={InstallationId} GitHubRepositoryId={GitHubRepositoryId} UserId={UserId}",
            projectId,
            source.Id,
            source.InstallationId,
            repositoryId,
            userId);

        return new LegacyInstallationRepositorySelection(
            source.Id,
            storedRepositoryId,
            authoritative.Id);
    }

    public async Task<bool> TryVerifyForLinkAsync(
        Guid userId,
        Guid projectId,
        Guid sourceId,
        IReadOnlyList<LinkGitHubRepositoryRequestItem> repositories,
        CancellationToken cancellationToken)
    {
        var source = await _store.GetSourceAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return false;
        }

        if (source.ProjectId != projectId)
        {
            throw Conflict("The GitHub access source does not belong to this project.");
        }

        // Defense in depth: linking already authorizes in RepositoryLinkService.
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);

        // Reject stale/tampered local selections before minting an installation
        // token or making repository API calls. Both direct and requested GitHub
        // App flows intentionally use the same multi-repository verification path.
        var storedSelections = new List<InstallationRepositorySelection>(repositories.Count);
        foreach (var selection in repositories)
        {
            var stored = await _store.GetSelectionAsync(
                sourceId,
                selection.GitHubRepositoryId,
                cancellationToken)
                ?? throw NotFound(
                    "The selected repository was not offered by this GitHub App access source.");
            storedSelections.Add(stored);
        }

        // Repository metadata access alone is not enough for ResearchTrack synchronization.
        // Fail before persistence with an actionable error if the installed App has not
        // been granted the read-only permissions required by commits and pull requests.
        var installation = await _gitHubAppClient.GetInstallationAsync(
            source.InstallationId,
            cancellationToken);
        GitHubAppPermissionRequirements.EnsureSynchronizationReadPermissions(installation);

        // Token creation proves the installation is currently accessible by this GitHub App.
        var token = await _gitHubAppClient.CreateInstallationTokenAsync(
            source.InstallationId,
            cancellationToken);

        foreach (var storedSelection in storedSelections)
        {
            var authoritative = await GetAccessibleRepositoryAsync(
                token,
                storedSelection.GitHubRepositoryId,
                cancellationToken);

            if (authoritative.Id != storedSelection.GitHubRepositoryId)
            {
                throw DependencyFailure("GitHub returned unexpected repository metadata.");
            }

            var verifiedRepositoryId = await _store.UpsertVerifiedAsync(
                source.Id,
                authoritative,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            if (verifiedRepositoryId != storedSelection.Id)
            {
                throw Conflict("The selected repository identity changed unexpectedly.");
            }

            _logger.LogInformation(
                "Verified GitHub App repository before linking. ProjectId={ProjectId} SourceId={SourceId} InstallationId={InstallationId} GitHubRepositoryId={GitHubRepositoryId} AccessType={AccessType} UserId={UserId}",
                projectId,
                source.Id,
                source.InstallationId,
                authoritative.Id,
                source.AccessType,
                userId);
        }

        return true;
    }

    private async Task<GitHubInstallationRepository> GetAccessibleRepositoryAsync(
        GitHubInstallationToken token,
        long repositoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _repositoryClient.GetAsync(
                token,
                repositoryId,
                cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            throw NotFound(
                "The selected repository is no longer accessible to this GitHub App installation.");
        }
    }

    private static ApiException NotFound(string message) => new(
        StatusCodes.Status404NotFound,
        ErrorCodes.NotFound,
        message);

    private static ApiException Conflict(string message) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.Conflict,
        message);

    private static ApiException DependencyFailure(string message) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message);
}
