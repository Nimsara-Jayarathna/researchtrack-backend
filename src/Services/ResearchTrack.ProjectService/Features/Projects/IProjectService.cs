using ResearchTrack.ProjectService.Contracts;

namespace ResearchTrack.ProjectService.Features.Projects;

public interface IProjectService
{
    Task<CreateProjectResponse> CreateAsync(
        Guid supervisorUserId,
        CreateProjectRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectSummaryResponse>>
        GetAccessibleProjectsAsync(
            Guid userId,
            string role,
            CancellationToken cancellationToken);

    Task<ProjectResponse?> GetAccessibleProjectAsync(
        Guid userId,
        string role,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ProjectResponse> UpdateAsync(
        Guid supervisorUserId,
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken);

    Task<ProjectResponse> UpdateLeaderAsync(
        Guid supervisorUserId,
        Guid projectId,
        UpdateProjectLeaderRequest request,
        CancellationToken cancellationToken);

    Task<ProjectResponse> AddMembersAsync(
        Guid supervisorUserId,
        Guid projectId,
        AddProjectMembersRequest request,
        CancellationToken cancellationToken);

    Task<ProjectResponse> RemoveStudentAsync(
        Guid supervisorUserId,
        Guid projectId,
        Guid studentId,
        CancellationToken cancellationToken);

    Task<ProjectMilestoneResponse> AddMilestoneAsync(
        Guid supervisorUserId,
        Guid projectId,
        CreateProjectMilestoneRequest request,
        CancellationToken cancellationToken);

    Task<ProjectMilestoneResponse> UpdateMilestoneAsync(
        Guid supervisorUserId,
        Guid projectId,
        Guid milestoneId,
        UpdateProjectMilestoneRequest request,
        CancellationToken cancellationToken);
}