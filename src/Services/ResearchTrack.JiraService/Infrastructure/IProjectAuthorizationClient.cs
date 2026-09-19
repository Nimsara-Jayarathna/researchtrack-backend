namespace ResearchTrack.JiraService.Infrastructure;
public interface IProjectAuthorizationClient { Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken); }
