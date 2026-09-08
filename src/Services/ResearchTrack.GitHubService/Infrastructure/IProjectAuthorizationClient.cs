namespace ResearchTrack.GitHubService.Infrastructure;

public interface IProjectAuthorizationClient
{
    Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken);
}
