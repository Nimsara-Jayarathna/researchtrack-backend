namespace ResearchTrack.AuthService.Infrastructure.Email;

public interface IPasswordResetEmailService
{
    Task SendResetLinkAsync(
        string to,
        string resetUrl,
        int expiryMinutes,
        CancellationToken cancellationToken);
}
