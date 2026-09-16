using System.Net;
using ResearchTrack.AuthService.Configuration;

namespace ResearchTrack.AuthService.Infrastructure.Email;

public sealed class PasswordResetEmailService : IPasswordResetEmailService
{
    private readonly IEmailProvider _emailProvider;
    private readonly EmailOptions _options;

    public PasswordResetEmailService(IEmailProvider emailProvider, EmailOptions options)
    {
        _emailProvider = emailProvider;
        _options = options;
    }

    public Task SendResetLinkAsync(
        string to,
        string resetUrl,
        int expiryMinutes,
        CancellationToken cancellationToken)
    {
        var safeUrl = WebUtility.HtmlEncode(resetUrl);
        var html = $"""
            <!doctype html><html><body style="font-family:Arial,sans-serif;color:#111827">
            <p>A password reset was requested for your {_options.SenderName} account.</p>
            <p><a href="{safeUrl}">Reset your password</a></p>
            <p>This single-use link expires in {expiryMinutes} minutes.</p>
            <p>If you did not request this change, you can safely ignore this email.</p>
            <p>Best regards,<br/>The {WebUtility.HtmlEncode(_options.SenderName)} Team</p>
            </body></html>
            """;

        return _emailProvider.SendAsync(
            to,
            $"Reset your {_options.SenderName} password",
            html,
            cancellationToken);
    }
}
