using ResearchTrack.AuthService.Contracts;

namespace ResearchTrack.AuthService.Features.Passwords;

public interface IPasswordResetService
{
    Task RequestResetAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken);

    Task<ValidateResetTokenResponse> ValidateTokenAsync(
        string? rawToken,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken);
}
