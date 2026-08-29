using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Users;

public interface IUserAccountService
{
    Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken);
}
