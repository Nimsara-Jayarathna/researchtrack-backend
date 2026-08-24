using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Registration;

public interface IRegistrationService
{
    // Existing ResearchTrack single-step endpoint: kept for backward compatibility.
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    // Multi-step registration flow used by the ResearchTrack web client.
    Task InitRegistrationAsync(string? email, CancellationToken cancellationToken);
    Task<RegisterVerifyResponse> VerifyOtpAsync(string? email, string? otp, CancellationToken cancellationToken);
    Task<RegistrationCompletionResult> CompleteRegistrationAsync(RegisterCompleteRequest request, CancellationToken cancellationToken);
    Task CleanupExpiredSessionsAndOtpsAsync(CancellationToken cancellationToken);
}
