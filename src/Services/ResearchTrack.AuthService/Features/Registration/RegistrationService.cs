using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.AuthService.Features.Registration;

public sealed class RegistrationService : IRegistrationService
{
    private readonly IDbContextFactory<AuthDbContext> _dbContextFactory;
    private readonly RegistrationOptions _registration;
    private readonly PasswordPolicyOptions _passwordPolicy;
    private readonly IPasswordHasher _passwordHasher;
    private readonly Regex _studentIdentifierRegex;

    public RegistrationService(
        IDbContextFactory<AuthDbContext> dbContextFactory,
        RegistrationOptions registration,
        PasswordPolicyOptions passwordPolicy,
        IPasswordHasher passwordHasher)
    {
        _dbContextFactory = dbContextFactory;
        _registration = registration;
        _passwordPolicy = passwordPolicy;
        _passwordHasher = passwordHasher;
        _studentIdentifierRegex = new Regex(
            registration.StudentIdentifierPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var firstName = request.EffectiveFirstName?.Trim();
        var lastName = request.EffectiveLastName?.Trim();
        var rawEmail = request.Email?.Trim();
        var password = request.Password;
        var registrationNumber = NormalizeRegistrationNumber(request.EffectiveRegistrationNumber);

        var errors = new List<ApiFieldError>();
        ValidateRequiredAndLengths(firstName, lastName, rawEmail, password, errors);

        string? normalizedEmail = null;
        string? emailLocalPart = null;
        UserRole? role = null;

        if (!string.IsNullOrWhiteSpace(rawEmail) && rawEmail.Length <= _registration.MaxEmailLength)
        {
            ValidateAndResolveEmail(rawEmail, errors, out normalizedEmail, out emailLocalPart, out role);
        }

        if (!string.IsNullOrEmpty(password))
        {
            ValidatePassword(password, errors);
        }

        if (role == UserRole.Student)
        {
            ValidateStudentIdentity(emailLocalPart, registrationNumber, errors);
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        var resolvedEmail = normalizedEmail!;
        var resolvedRole = role!.Value;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await dbContext.Users.AnyAsync(user => user.Email == resolvedEmail, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "An account with this email already exists.",
                new { field = "email" });
        }

        if (resolvedRole == UserRole.Student && registrationNumber is not null
            && await dbContext.Users.AnyAsync(user => user.RegistrationNumber == registrationNumber, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "An account with this registration number already exists.",
                new { field = "registrationNumber" });
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = resolvedEmail,
            FirstName = firstName!,
            LastName = lastName!,
            PasswordHash = _passwordHasher.Hash(password!),
            Role = resolvedRole,
            RegistrationNumber = resolvedRole == UserRole.Student ? registrationNumber : null,
            CreatedAt = now
        };

        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "An account with this email or registration number already exists.",
                innerException: exception);
        }

        return new RegisterResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.RegistrationNumber,
            ToApiRole(user.Role),
            user.CreatedAt);
    }

    private void ValidateRequiredAndLengths(
        string? firstName,
        string? lastName,
        string? email,
        string? password,
        ICollection<ApiFieldError> errors)
    {
        AddRequiredOrLengthError(errors, "firstName", firstName, _registration.MaxFirstNameLength);
        AddRequiredOrLengthError(errors, "lastName", lastName, _registration.MaxLastNameLength);
        AddRequiredOrLengthError(errors, "email", email, _registration.MaxEmailLength);

        if (string.IsNullOrWhiteSpace(password))
        {
            AddError(errors, "password", "Password is required.");
        }
    }

    private void ValidateAndResolveEmail(
        string email,
        ICollection<ApiFieldError> errors,
        out string? normalizedEmail,
        out string? localPart,
        out UserRole? role)
    {
        normalizedEmail = null;
        localPart = null;
        role = null;

        MailAddress parsed;
        try
        {
            parsed = new MailAddress(email);
        }
        catch (FormatException)
        {
            AddError(errors, "email", "Email must be a valid email address.");
            return;
        }

        if (!parsed.Address.Equals(email, StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, "email", "Email must be a valid email address.");
            return;
        }

        var atIndex = parsed.Address.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == parsed.Address.Length - 1)
        {
            AddError(errors, "email", "Email must be a valid email address.");
            return;
        }

        localPart = parsed.Address[..atIndex];
        var domain = parsed.Address[(atIndex + 1)..].ToLowerInvariant();

        if (domain.Equals(_registration.StudentEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            role = UserRole.Student;
            if (!_studentIdentifierRegex.IsMatch(localPart))
            {
                AddError(errors, "email", "Student email identifier does not match the configured institutional pattern.");
            }
        }
        else if (domain.Equals(_registration.SupervisorEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            role = UserRole.Supervisor;
        }
        else
        {
            AddError(errors, "email", "Email does not match an allowed institutional domain.");
            return;
        }

        normalizedEmail = parsed.Address.ToLowerInvariant();
    }

    private void ValidateStudentIdentity(
        string? emailLocalPart,
        string? registrationNumber,
        ICollection<ApiFieldError> errors)
    {
        if (_registration.RequireStudentRegistrationNumber && string.IsNullOrWhiteSpace(registrationNumber))
        {
            AddError(errors, "registrationNumber", "Registration number is required for student accounts.");
            return;
        }

        if (registrationNumber is null)
        {
            return;
        }

        if (registrationNumber.Length > _registration.MaxRegistrationNumberLength)
        {
            AddError(errors, "registrationNumber", $"Registration number must not exceed {_registration.MaxRegistrationNumberLength} characters.");
            return;
        }

        if (!_studentIdentifierRegex.IsMatch(registrationNumber))
        {
            AddError(errors, "registrationNumber", "Registration number does not match the configured institutional pattern.");
            return;
        }

        if (_registration.RequireStudentRegistrationNumberToMatchEmail
            && !string.Equals(emailLocalPart, registrationNumber, StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, "registrationNumber", "Registration number must match the student email identifier.");
        }
    }

    private void ValidatePassword(string password, ICollection<ApiFieldError> errors)
    {
        if (password.Length < _passwordPolicy.MinimumLength)
        {
            AddError(errors, "password", $"Password must be at least {_passwordPolicy.MinimumLength} characters.");
        }
        if (password.Length > _passwordPolicy.MaximumLength)
        {
            AddError(errors, "password", $"Password must not exceed {_passwordPolicy.MaximumLength} characters.");
        }
        if (_passwordPolicy.RequireUppercase && !password.Any(char.IsUpper))
        {
            AddError(errors, "password", "Password must contain an uppercase letter.");
        }
        if (_passwordPolicy.RequireLowercase && !password.Any(char.IsLower))
        {
            AddError(errors, "password", "Password must contain a lowercase letter.");
        }
        if (_passwordPolicy.RequireDigit && !password.Any(char.IsDigit))
        {
            AddError(errors, "password", "Password must contain a digit.");
        }
        if (_passwordPolicy.RequireSpecialCharacter && !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            AddError(errors, "password", "Password must contain a special character.");
        }
    }

    private static void AddRequiredOrLengthError(
        ICollection<ApiFieldError> errors,
        string field,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, field, $"{ToDisplayName(field)} is required.");
        }
        else if (value.Length > maxLength)
        {
            AddError(errors, field, $"{ToDisplayName(field)} must not exceed {maxLength} characters.");
        }
    }

    private static void AddError(ICollection<ApiFieldError> errors, string field, string message)
    {
        errors.Add(new ApiFieldError(field, new[] { message }));
    }

    private static string? NormalizeRegistrationNumber(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string ToApiRole(UserRole role) => role switch
    {
        UserRole.Student => "STUDENT",
        UserRole.Supervisor => "SUPERVISOR",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    private static string ToDisplayName(string field) => field switch
    {
        "firstName" => "First name",
        "lastName" => "Last name",
        "email" => "Email",
        _ => field
    };

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
