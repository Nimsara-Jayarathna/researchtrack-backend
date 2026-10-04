namespace ResearchTrack.SubmissionService.Infrastructure;

public interface IProjectAuthorizationClient
{
    Task EnsureCanAccessAsync(Guid projectId, CancellationToken cancellationToken);
    Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ProjectSubmissionContext> GetSubmissionContextAsync(Guid projectId, CancellationToken cancellationToken);
}
