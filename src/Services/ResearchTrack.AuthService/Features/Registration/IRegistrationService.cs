using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Registration;

public interface IRegistrationService
{
    // Existing ResearchTrack single-step endpoint: kept for backward compatibility.
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    // SuperviseSuite registration flow ported as-is.
    Task InitRegistrationAsync(string? email, CancellationToken cancellationToken);
    Task<RegisterVerifyResponse> VerifyOtpAsync(string? email, string? otp, CancellationToken cancellationToken);
    Task<RegistrationCompletionResult> CompleteRegistrationAsync(RegisterCompleteRequest request, CancellationToken cancellationToken);
    Task CleanupExpiredSessionsAndOtpsAsync(CancellationToken cancellationToken);
}
