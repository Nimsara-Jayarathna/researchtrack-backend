namespace ResearchTrack.MeetingService.Infrastructure;

public interface IProjectAuthorizationClient
{
    Task EnsureCanAccessAsync(Guid projectId, CancellationToken cancellationToken);
    Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken);
}
