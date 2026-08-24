namespace ResearchTrack.AuthService.Infrastructure.Email;

public interface IEmailProvider
{
    Task SendAsync(string to, string subject, string htmlContent, CancellationToken cancellationToken);
}
