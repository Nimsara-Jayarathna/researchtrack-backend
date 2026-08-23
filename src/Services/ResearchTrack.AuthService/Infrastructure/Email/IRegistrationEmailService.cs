namespace ResearchTrack.AuthService.Infrastructure.Email;

public interface IRegistrationEmailService
{
    Task SendOtpEmailAsync(string to, string otp, CancellationToken cancellationToken);
    Task SendRegistrationSuccessEmailAsync(string to, string firstName, CancellationToken cancellationToken);
}
