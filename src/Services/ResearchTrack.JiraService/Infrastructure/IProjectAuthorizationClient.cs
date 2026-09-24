namespace ResearchTrack.JiraService.Infrastructure;
public interface IProjectAuthorizationClient { Task EnsureCanManageAsync(Guid projectId,CancellationToken cancellationToken); Task EnsureCanAccessAsync(Guid projectId,CancellationToken cancellationToken); }
