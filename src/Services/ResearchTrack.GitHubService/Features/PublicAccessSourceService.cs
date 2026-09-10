using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Features;

public sealed class PublicAccessSourceService : IPublicAccessSourceService
{
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IGitHubPublicRepositoryClient _gitHubClient;
    private readonly IPublicAccessSourceStore _store;
    private readonly TimeProvider _timeProvider;

    public PublicAccessSourceService(
        IProjectAuthorizationClient projectAuthorization,
        IGitHubPublicRepositoryClient gitHubClient,
        IPublicAccessSourceStore store,
        TimeProvider timeProvider)
    {
        _projectAuthorization = projectAuthorization;
        _gitHubClient = gitHubClient;
        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<GitHubAvailableRepositoriesResponse> CreateAsync(
        Guid userId,
        CreatePublicAccessSourceRequest request,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(request.ProjectId, cancellationToken);
        var address = GitHubRepositoryUrlParser.Parse(request.RepositoryUrl);
        var repository = await _gitHubClient.GetAsync(address.Owner, address.Repository, cancellationToken);

        return await _store.CreateAsync(
            request.ProjectId,
            userId,
            repository,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    public async Task<GitHubAvailableRepositoriesResponse> GetAvailableAsync(
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

        var available = await _store.GetAvailableAsync(sourceId, cancellationToken)
            ?? throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The active GitHub access source was not found.");

        await _projectAuthorization.EnsureCanManageAsync(
            available.ProjectId,
            cancellationToken);

        return available.Response;
    }
}
