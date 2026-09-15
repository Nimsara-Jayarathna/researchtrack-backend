using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubInstallationRepositoryInventoryService : IGitHubInstallationRepositoryInventoryService
{
    private const int PageSize = 100;
    private const int MaxPages = 100;

    private readonly IInstallationRepositoryStore _store;
    private readonly IGitHubAppClient _gitHubAppClient;
    private readonly IGitHubInstallationRepositoryClient _repositoryClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubInstallationRepositoryInventoryService> _logger;

    public GitHubInstallationRepositoryInventoryService(
        IInstallationRepositoryStore store,
        IGitHubAppClient gitHubAppClient,
        IGitHubInstallationRepositoryClient repositoryClient,
        TimeProvider timeProvider,
        ILogger<GitHubInstallationRepositoryInventoryService> logger)
    {
        _store = store;
        _gitHubAppClient = gitHubAppClient;
        _repositoryClient = repositoryClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitHubAvailableRepositoriesResponse?> TryRefreshAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Access source id is required.", nameof(sourceId));
        }

        var source = await _store.GetSourceAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var installation = await _gitHubAppClient.GetInstallationAsync(
            source.InstallationId,
            cancellationToken);

        if (installation.InstallationId != source.InstallationId
            || !string.Equals(installation.OwnerLogin, source.OwnerLogin, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(installation.OwnerType, source.OwnerType, StringComparison.Ordinal))
        {
            throw DependencyFailure(
                "GitHub installation verification returned metadata that does not match the stored access source.");
        }

        var token = await _gitHubAppClient.CreateInstallationTokenAsync(
            source.InstallationId,
            cancellationToken);
        var repositories = await ListAllRepositoriesAsync(token, cancellationToken);

        var response = await _store.UpsertAvailableAsync(
            source.Id,
            repositories,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        _logger.LogInformation(
            "Refreshed GitHub App installation repository inventory. ProjectId={ProjectId} SourceId={SourceId} InstallationId={InstallationId} RepositoryCount={RepositoryCount} AccessType={AccessType}",
            source.ProjectId,
            source.Id,
            source.InstallationId,
            response.TotalCount,
            source.AccessType);

        return response;
    }

    private async Task<IReadOnlyList<GitHubInstallationRepository>> ListAllRepositoriesAsync(
        GitHubInstallationToken token,
        CancellationToken cancellationToken)
    {
        var repositories = new List<GitHubInstallationRepository>();
        int? expectedTotalCount = null;

        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await _repositoryClient.ListAsync(
                token,
                page,
                PageSize,
                cancellationToken);

            expectedTotalCount ??= result.TotalCount;
            if (result.TotalCount != expectedTotalCount)
            {
                throw DependencyFailure(
                    "GitHub repository pagination changed during repository discovery.");
            }

            repositories.AddRange(result.Items);

            if (!result.HasNext)
            {
                if (repositories.Count != result.TotalCount
                    || repositories.Select(repository => repository.Id).Distinct().Count()
                        != repositories.Count)
                {
                    throw DependencyFailure(
                        "GitHub repository pagination returned an inconsistent repository inventory.");
                }

                return repositories;
            }
        }

        throw DependencyFailure(
            "GitHub installation repository inventory exceeds the supported pagination limit.");
    }

    private static ApiException DependencyFailure(string message) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message);
}
