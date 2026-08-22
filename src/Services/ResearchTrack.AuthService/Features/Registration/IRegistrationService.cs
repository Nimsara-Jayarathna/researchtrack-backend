using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Registration;

public interface IRegistrationService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
}
