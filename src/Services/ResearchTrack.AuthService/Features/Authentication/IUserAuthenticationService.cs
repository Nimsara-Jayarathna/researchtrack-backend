using System.Security.Claims;
using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Authentication;

public interface IUserAuthenticationService
{
    Task<AuthSessionResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthSessionResult> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken);
    Task RevokeRefreshTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken);
    Task<LoginResponse> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
