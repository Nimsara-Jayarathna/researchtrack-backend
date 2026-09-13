using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Infrastructure.Email;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.AuthService.Features.Passwords;

public sealed class PasswordResetService : IPasswordResetService
{
    private const int MaximumTokenLength = 512;
    private const string InvalidTokenMessage =
        "Reset token is invalid, expired, or has already been used.";

    private readonly IDbContextFactory<AuthDbContext> _dbContextFactory;
    private readonly RegistrationOptions _registrationOptions;
    private readonly PasswordResetOptions _resetOptions;
    private readonly IPasswordPolicyValidator _passwordPolicyValidator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPasswordResetEmailService _emailService;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        IDbContextFactory<AuthDbContext> dbContextFactory,
        RegistrationOptions registrationOptions,
        PasswordResetOptions resetOptions,
        IPasswordPolicyValidator passwordPolicyValidator,
        IPasswordHasher passwordHasher,
        IPasswordResetEmailService emailService,
        ILogger<PasswordResetService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _registrationOptions = registrationOptions;
        _resetOptions = resetOptions;
        _passwordPolicyValidator = passwordPolicyValidator;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task RequestResetAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedEmail = NormalizeEmail(request.Email);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Email == normalizedEmail, cancellationToken);

        // A caller receives the same response for registered and unknown emails.
        if (user is null)
        {
            _ = HashToken(GenerateRawToken());
            return;
        }

        var now = DateTime.UtcNow;
        var rawToken = GenerateRawToken();
        var resetToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = now.AddMinutes(_resetOptions.TokenExpiryMinutes),
            CreatedAt = now
        };

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await dbContext.PasswordResetTokens
                .Where(token => token.UserId == user.Id && token.UsedAt == null)
                .ExecuteUpdateAsync(
                    updates => updates.SetProperty(token => token.UsedAt, now),
                    cancellationToken);

            dbContext.PasswordResetTokens.Add(resetToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        try
        {
            await _emailService.SendResetLinkAsync(
                user.Email,
                _resetOptions.CreateResetUrl(rawToken),
                _resetOptions.TokenExpiryMinutes,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Preserve the non-enumerating response while making the undelivered
            // token unusable and retaining an operational signal in server logs.
            await dbContext.PasswordResetTokens
                .Where(token => token.Id == resetToken.Id && token.UsedAt == null)
                .ExecuteUpdateAsync(
                    updates => updates.SetProperty(token => token.UsedAt, DateTime.UtcNow),
                    cancellationToken);
            _logger.LogError(exception, "Password reset email delivery failed.");
        }
    }

    public async Task<ValidateResetTokenResponse> ValidateTokenAsync(
        string? rawToken,
        CancellationToken cancellationToken)
    {
        if (!IsTokenShapeValid(rawToken))
        {
            return new ValidateResetTokenResponse(false);
        }

        var tokenHash = HashToken(rawToken!);
        var now = DateTime.UtcNow;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var valid = await dbContext.PasswordResetTokens
            .AsNoTracking()
            .AnyAsync(
                token => token.TokenHash == tokenHash
                    && token.UsedAt == null
                    && token.ExpiresAt > now,
                cancellationToken);

        return new ValidateResetTokenResponse(valid);
    }

    public async Task ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ApiFieldError>();
        if (!IsTokenShapeValid(request.Token))
        {
            errors.Add(new ApiFieldError("token", [InvalidTokenMessage]));
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            errors.Add(new ApiFieldError("newPassword", ["New password is required."]));
        }
        else
        {
            errors.AddRange(
                _passwordPolicyValidator.Validate(request.NewPassword, "newPassword"));
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        var tokenHash = HashToken(request.Token!);
        var now = DateTime.UtcNow;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var resetToken = await dbContext.PasswordResetTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(
                token => token.TokenHash == tokenHash
                    && token.UsedAt == null
                    && token.ExpiresAt > now,
                cancellationToken);

        if (resetToken is null)
        {
            throw InvalidToken();
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == resetToken.UserId,
            cancellationToken);
        if (user is null)
        {
            throw InvalidToken();
        }

        if (_passwordHasher.Verify(request.NewPassword!, user.PasswordHash))
        {
            throw new ApiValidationException(
                [new ApiFieldError(
                    "newPassword",
                    ["New password must be different from current password."])]);
        }

        var claimed = await dbContext.PasswordResetTokens
            .Where(token => token.Id == resetToken.Id
                && token.UsedAt == null
                && token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(token => token.UsedAt, now),
                cancellationToken);
        if (claimed != 1)
        {
            throw InvalidToken();
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword!);
        user.UpdatedAt = now;

        await dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.Id && token.UsedAt == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(token => token.UsedAt, now),
                cancellationToken);

        await dbContext.RefreshTokens
            .Where(token => token.UserId == user.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(token => token.RevokedAt, now),
                cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw Validation("email", "Email is required.");
        }

        var trimmed = email.Trim();
        if (trimmed.Length > _registrationOptions.MaxEmailLength)
        {
            throw Validation(
                "email",
                $"Email must not exceed {_registrationOptions.MaxEmailLength} characters.");
        }

        try
        {
            var parsed = new MailAddress(trimmed);
            if (!parsed.Address.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }

            return parsed.Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw Validation("email", "Email must be a valid email address.");
        }
    }

    private static bool IsTokenShapeValid(string? token) =>
        !string.IsNullOrWhiteSpace(token) && token.Length <= MaximumTokenLength;

    private static string GenerateRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashToken(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static ApiValidationException InvalidToken() =>
        Validation("token", InvalidTokenMessage);

    private static ApiValidationException Validation(string field, string message) =>
        new([new ApiFieldError(field, [message])]);
}
