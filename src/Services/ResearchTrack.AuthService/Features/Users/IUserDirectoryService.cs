using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Users;

public interface IUserDirectoryService
{
    Task<IReadOnlyList<UserDirectoryResponse>> SearchStudentsAsync(
        string? query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserDirectoryResponse>> ResolveStudentsAsync(
        IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken);

    Task<UserDirectoryResponse?> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
