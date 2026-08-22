using ResearchTrack.ProjectService.Contracts;

namespace ResearchTrack.ProjectService.Features.Projects;

public interface IProjectService
{
    Task<ProjectResponse> CreateAsync(Guid supervisorUserId, CreateProjectRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectSummaryResponse>> GetOwnedProjectsAsync(Guid supervisorUserId, CancellationToken cancellationToken);
    Task<ProjectResponse?> GetOwnedProjectAsync(Guid supervisorUserId, Guid projectId, CancellationToken cancellationToken);
}
