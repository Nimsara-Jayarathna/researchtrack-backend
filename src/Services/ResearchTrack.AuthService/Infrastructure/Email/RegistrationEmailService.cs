using System.Net;
using ResearchTrack.AuthService.Configuration;

namespace ResearchTrack.AuthService.Infrastructure.Email;

public sealed class RegistrationEmailService : IRegistrationEmailService
{
    private readonly IEmailProvider _emailProvider;
    private readonly EmailOptions _options;
    private readonly RegistrationOptions _registration;

    public RegistrationEmailService(IEmailProvider emailProvider, EmailOptions options, RegistrationOptions registration)
    {
        _emailProvider = emailProvider;
        _options = options;
        _registration = registration;
    }

    public Task SendOtpEmailAsync(string to, string otp, CancellationToken cancellationToken)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(_registration.OtpExpirySeconds / 60d));
        var html = $"""
            <!doctype html><html><body style="font-family:Arial,sans-serif;color:#111827">
            <p>Use the code below to verify your email address. It expires in {minutes} minutes.</p>
            <p style="font-family:monospace;font-size:32px;font-weight:700;letter-spacing:6px">{WebUtility.HtmlEncode(otp)}</p>
            <p>If you did not request this code, you can safely ignore this email.</p>
            <p>Best regards,<br/>The {WebUtility.HtmlEncode(_options.SenderName)} Team</p>
            </body></html>
            """;
        return _emailProvider.SendAsync(to, $"Your {_options.SenderName} verification code", html, cancellationToken);
    }

    public Task SendRegistrationSuccessEmailAsync(string to, string firstName, CancellationToken cancellationToken)
    {
        var html = $"""
            <!doctype html><html><body style="font-family:Arial,sans-serif;color:#111827">
            <p>Hello <strong>{WebUtility.HtmlEncode(firstName)}</strong>,</p>
            <p>Your {_options.SenderName} account has been created successfully.</p>
            <p>You can now sign in and start using the platform.</p>
            <p>Best regards,<br/>The {WebUtility.HtmlEncode(_options.SenderName)} Team</p>
            </body></html>
            """;
        return _emailProvider.SendAsync(to, $"Registration completed - {_options.SenderName}", html, cancellationToken);
    }
}
