namespace ResearchTrack.GitHubService.Infrastructure;

public interface IProjectAuthorizationClient
{
    Task EnsureCanViewAsync(Guid projectId, CancellationToken cancellationToken);
    Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken);
}
