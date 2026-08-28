using ResearchTrack.ProjectService.Contracts;

namespace ResearchTrack.ProjectService.Features.Projects;

public interface IProjectService
{
    Task<CreateProjectResponse> CreateAsync(
        Guid supervisorUserId,
        CreateProjectRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectSummaryResponse>> GetAccessibleProjectsAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken);

    Task<ProjectResponse?> GetAccessibleProjectAsync(
        Guid userId,
        string role,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<SupervisorDashboardResponse> GetSupervisorDashboardAsync(
        Guid supervisorUserId,
        CancellationToken cancellationToken);
}