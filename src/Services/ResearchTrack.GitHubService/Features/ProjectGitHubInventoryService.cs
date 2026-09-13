using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features;

public sealed class ProjectGitHubInventoryService : IProjectGitHubInventoryService
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IGitHubInstallationRepositoryService _installationRepositoryService;
    private readonly IPublicAccessSourceService _publicAccessSourceService;

    public ProjectGitHubInventoryService(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        IProjectAuthorizationClient projectAuthorization,
        IGitHubInstallationRepositoryService installationRepositoryService,
        IPublicAccessSourceService publicAccessSourceService)
    {
        _dbContextFactory = dbContextFactory;
        _projectAuthorization = projectAuthorization;
        _installationRepositoryService = installationRepositoryService;
        _publicAccessSourceService = publicAccessSourceService;
    }

    public async Task<ProjectGitHubRepositoryListingResponse> GetAsync(
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
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sources = await dbContext.AccessSources
            .AsNoTracking()
            .Where(source => source.ProjectId == projectId && source.Active)
            .OrderBy(source => source.CreatedAt)
            .Select(source => new { source.Id, source.AccessType })
            .ToListAsync(cancellationToken);

        var inventory = new List<GitHubAvailableRepositoriesResponse>(sources.Count);
        foreach (var source in sources)
        {
            if (string.Equals(
                source.AccessType,
                GitHubAccessTypes.PublicUrl,
                StringComparison.Ordinal))
            {
                inventory.Add(await _publicAccessSourceService.GetAvailableAsync(
                    userId,
                    source.Id,
                    cancellationToken));
                continue;
            }

            var available = await _installationRepositoryService.TryGetAvailableAsync(
                userId,
                source.Id,
                cancellationToken);
            if (available is not null)
            {
                inventory.Add(available);
            }
        }

        return new ProjectGitHubRepositoryListingResponse(projectId, inventory);
    }
}
