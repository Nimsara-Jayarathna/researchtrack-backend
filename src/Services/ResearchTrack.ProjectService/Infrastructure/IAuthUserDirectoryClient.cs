namespace ResearchTrack.ProjectService.Infrastructure;

public interface IAuthUserDirectoryClient
{
    Task<AuthDirectoryUser> GetCurrentUserAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthDirectoryUser>> ResolveStudentsAsync(
        IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken);
}
