using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Infrastructure.Email;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Infrastructure.Tokens;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.AuthService.Features.Registration;

public sealed class RegistrationService : IRegistrationService
{
    private const string RegistrationTokenPrefix = "token_";

    private readonly IDbContextFactory<AuthDbContext> _dbContextFactory;
    private readonly RegistrationOptions _registration;
    private readonly PasswordPolicyOptions _passwordPolicy;
    private readonly JwtOptions _jwtOptions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRegistrationEmailService _emailService;
    private readonly ILogger<RegistrationService> _logger;
    private readonly Regex _studentIdentifierRegex;

    public RegistrationService(
        IDbContextFactory<AuthDbContext> dbContextFactory,
        RegistrationOptions registration,
        PasswordPolicyOptions passwordPolicy,
        JwtOptions jwtOptions,
        IPasswordHasher passwordHasher,
        IAccessTokenService accessTokenService,
        IRegistrationEmailService emailService,
        ILogger<RegistrationService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _registration = registration;
        _passwordPolicy = passwordPolicy;
        _jwtOptions = jwtOptions;
        _passwordHasher = passwordHasher;
        _accessTokenService = accessTokenService;
        _emailService = emailService;
        _logger = logger;
        _studentIdentifierRegex = new Regex(
            registration.StudentIdentifierPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    // Existing ResearchTrack endpoint. Kept unchanged in purpose/contract.
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
            ValidateAndResolveEmailForDirectRegistration(rawEmail, errors, out normalizedEmail, out emailLocalPart, out role);
        }

        if (!string.IsNullOrEmpty(password))
        {
            ValidatePassword(password, errors);
        }

        if (role == UserRole.Student)
        {
            ValidateStudentIdentity(emailLocalPart, registrationNumber, errors, "registrationNumber");
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        var resolvedEmail = normalizedEmail!;
        var resolvedRole = role!.Value;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureNoDuplicateUserAsync(dbContext, resolvedEmail, resolvedRole, registrationNumber, cancellationToken);

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
            throw Conflict("An account with this email or registration number already exists.", exception);
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

    public async Task InitRegistrationAsync(string? email, CancellationToken cancellationToken)
    {
        var normalizedEmail = ValidateRegistrationEmail(email);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken))
        {
            throw Conflict("An account with this email already exists.");
        }

        var rawOtp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        var now = DateTime.UtcNow;
        var otp = new EmailOtp
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            OtpHash = Sha256Base64(rawOtp),
            ExpiresAt = now.AddSeconds(_registration.OtpExpirySeconds),
            CreatedAt = now
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.EmailOtps.Add(otp);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Matches SuperviseSuite semantics: if OTP delivery fails, initiation fails.
        await _emailService.SendOtpEmailAsync(normalizedEmail, rawOtp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<RegisterVerifyResponse> VerifyOtpAsync(
        string? email,
        string? otp,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = ValidateBasicEmail(email, "email");
        if (string.IsNullOrWhiteSpace(otp) || !Regex.IsMatch(otp, "^[0-9]{6}$", RegexOptions.CultureInvariant))
        {
            throw Validation("otp", "OTP must be exactly 6 digits.");
        }

        var now = DateTime.UtcNow;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var emailOtp = await dbContext.EmailOtps
            .Where(candidate => candidate.Email == normalizedEmail
                && candidate.UsedAt == null
                && candidate.ExpiresAt > now)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (emailOtp is null || !HashesEqual(emailOtp.OtpHash, Sha256Base64(otp)))
        {
            throw Validation("otp", "Invalid or expired OTP.");
        }

        emailOtp.UsedAt = now;

        var resolvedRole = InferRole(normalizedEmail);
        var rawToken = GenerateRawToken();
        var session = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            TokenHash = Sha256Base64(rawToken),
            Email = normalizedEmail,
            Role = resolvedRole,
            ExpiresAt = now.AddSeconds(_registration.SessionExpirySeconds),
            CreatedAt = now
        };

        dbContext.RegistrationSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterVerifyResponse(
            RegistrationTokenPrefix + rawToken,
            resolvedRole is null,
            resolvedRole is null ? null : ToApiRole(resolvedRole.Value));
    }

    public async Task<RegistrationCompletionResult> CompleteRegistrationAsync(
        RegisterCompleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ApiFieldError>();
        var token = request.RegistrationToken?.Trim();
        var firstName = request.Fname?.Trim();
        var lastName = request.Lname?.Trim();
        var password = request.Password;
        var registrationNumber = NormalizeRegistrationNumber(request.Name);

        AddRequiredOrLengthError(errors, "registrationToken", token, 512);
        AddRequiredOrLengthError(errors, "fname", firstName, _registration.MaxFirstNameLength);
        AddRequiredOrLengthError(errors, "lname", lastName, _registration.MaxLastNameLength);
        if (string.IsNullOrWhiteSpace(password))
        {
            AddError(errors, "password", "Password is required.");
        }
        else
        {
            ValidatePassword(password, errors);
        }

        if (registrationNumber is not null && registrationNumber.Length > _registration.MaxRegistrationNumberLength)
        {
            AddError(errors, "name", $"Registration number must not exceed {_registration.MaxRegistrationNumberLength} characters.");
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        var rawToken = token!.StartsWith(RegistrationTokenPrefix, StringComparison.Ordinal)
            ? token[RegistrationTokenPrefix.Length..]
            : token;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var session = await dbContext.RegistrationSessions
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == Sha256Base64(rawToken), cancellationToken);

        if (session is null)
        {
            throw Validation("registrationToken", "Invalid or expired registration token.");
        }

        var now = DateTime.UtcNow;
        if (session.UsedAt is not null)
        {
            throw Validation("registrationToken", "Registration token already used.");
        }
        if (session.ExpiresAt < now)
        {
            throw Validation("registrationToken", "Registration token has expired.");
        }

        var effectiveRole = ResolveEffectiveRole(session.Role, request.Role);
        if (await dbContext.Users.AnyAsync(user => user.Email == session.Email, cancellationToken))
        {
            throw Conflict("An account with this email already exists.");
        }

        if (effectiveRole == UserRole.Student)
        {
            var studentErrors = new List<ApiFieldError>();
            ValidateStudentIdentity(ExtractEmailLocalPart(session.Email), registrationNumber, studentErrors, "name");
            if (studentErrors.Count > 0)
            {
                throw new ApiValidationException(studentErrors);
            }

            if (registrationNumber is not null
                && await dbContext.Users.AnyAsync(user => user.RegistrationNumber == registrationNumber, cancellationToken))
            {
                throw Conflict("An account with this registration number already exists.");
            }
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = session.Email,
            FirstName = firstName!,
            LastName = lastName!,
            PasswordHash = _passwordHasher.Hash(password!),
            Role = effectiveRole,
            RegistrationNumber = effectiveRole == UserRole.Student ? registrationNumber : null,
            CreatedAt = now
        };

        var rawRefreshToken = GenerateRawToken();
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Sha256Base64(rawRefreshToken),
            ExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays),
            CreatedAt = now
        };

        session.UsedAt = now;
        dbContext.Users.Add(user);
        dbContext.RefreshTokens.Add(refreshToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw Conflict("Email or registration number already exists.", exception);
        }

        var accessToken = _accessTokenService.Generate(user);

        try
        {
            await _emailService.SendRegistrationSuccessEmailAsync(user.Email, user.FirstName, cancellationToken);
        }
        catch (Exception exception)
        {
            // Matches SuperviseSuite: account creation succeeds even if the success email fails.
            _logger.LogError(exception, "Failed to send registration success email to {Email}", user.Email);
        }

        var response = new RegistrationCompleteResponse(
            new RegistrationUserResponse(user.Id, user.Email, user.FirstName, user.LastName, ToApiRole(user.Role)));

        return new RegistrationCompletionResult(accessToken, rawRefreshToken, response);
    }

    public async Task CleanupExpiredSessionsAndOtpsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.EmailOtps
            .Where(otp => otp.ExpiresAt < now || otp.UsedAt != null)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.RegistrationSessions
            .Where(session => session.ExpiresAt < now || session.UsedAt != null)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private string ValidateRegistrationEmail(string? email)
    {
        var normalizedEmail = ValidateBasicEmail(email, "email");

        if (!_registration.IsEmailAllowed(normalizedEmail))
        {
            throw Validation("email", "Email domain not permitted for registration.");
        }

        if (_registration.EffectiveStudentEmailPrefixRestrictionEnabled
            && InferRole(normalizedEmail) == UserRole.Student)
        {
            var localPart = ExtractEmailLocalPart(normalizedEmail);
            if (localPart is null || !_studentIdentifierRegex.IsMatch(localPart))
            {
                throw Validation("email", "Invalid IT number format. Use ITXXXXXXXX.");
            }
        }

        return normalizedEmail;
    }

    private string ValidateBasicEmail(string? email, string field)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw Validation(field, "Email is required.");
        }

        var trimmed = email.Trim();
        if (trimmed.Length > _registration.MaxEmailLength)
        {
            throw Validation(field, $"Email must not exceed {_registration.MaxEmailLength} characters.");
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
            throw Validation(field, "Email must be a valid email address.");
        }
    }

    private UserRole? InferRole(string normalizedEmail) => _registration.InferRole(normalizedEmail) switch
    {
        UserRoleResolution.Student => UserRole.Student,
        UserRoleResolution.Supervisor => UserRole.Supervisor,
        _ => null
    };

    private UserRole ResolveEffectiveRole(UserRole? sessionRole, string? requestedRole)
    {
        if (sessionRole is not null)
        {
            return sessionRole.Value;
        }

        if (string.IsNullOrWhiteSpace(requestedRole))
        {
            throw Validation("role", "Role is required.");
        }

        return requestedRole.Trim().ToUpperInvariant() switch
        {
            "STUDENT" => UserRole.Student,
            "SUPERVISOR" => UserRole.Supervisor,
            _ => throw Validation("role", "Role must be STUDENT or SUPERVISOR.")
        };
    }

    private void ValidateAndResolveEmailForDirectRegistration(
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

        localPart = ExtractEmailLocalPart(parsed.Address);
        var resolvedRole = InferRole(parsed.Address.ToLowerInvariant());
        if (resolvedRole is null)
        {
            AddError(errors, "email", "Email does not match an allowed institutional domain.");
            return;
        }

        role = resolvedRole;
        if (role == UserRole.Student && !_studentIdentifierRegex.IsMatch(localPart ?? string.Empty))
        {
            AddError(errors, "email", "Student email identifier does not match the configured institutional pattern.");
        }

        normalizedEmail = parsed.Address.ToLowerInvariant();
    }

    private void ValidateStudentIdentity(string? emailLocalPart, string? registrationNumber, ICollection<ApiFieldError> errors, string field)
    {
        if (_registration.RequireStudentRegistrationNumber && string.IsNullOrWhiteSpace(registrationNumber))
        {
            AddError(errors, field, "Registration number is required for student accounts.");
            return;
        }

        if (registrationNumber is null)
        {
            return;
        }

        if (registrationNumber.Length > _registration.MaxRegistrationNumberLength)
        {
            AddError(errors, field, $"Registration number must not exceed {_registration.MaxRegistrationNumberLength} characters.");
            return;
        }

        if (_registration.EffectiveStudentEmailPrefixRestrictionEnabled && !_studentIdentifierRegex.IsMatch(registrationNumber))
        {
            AddError(errors, field, "Invalid IT number format. Use ITXXXXXXXX.");
            return;
        }

        if (_registration.EffectiveStudentEmailPrefixRestrictionEnabled
            && _registration.RequireStudentRegistrationNumberToMatchEmail
            && !string.Equals(emailLocalPart, registrationNumber, StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, field, "Registration number must match student email ID.");
        }
    }

    private void ValidateRequiredAndLengths(string? firstName, string? lastName, string? email, string? password, ICollection<ApiFieldError> errors)
    {
        AddRequiredOrLengthError(errors, "firstName", firstName, _registration.MaxFirstNameLength);
        AddRequiredOrLengthError(errors, "lastName", lastName, _registration.MaxLastNameLength);
        AddRequiredOrLengthError(errors, "email", email, _registration.MaxEmailLength);
        if (string.IsNullOrWhiteSpace(password)) AddError(errors, "password", "Password is required.");
    }

    private void ValidatePassword(string password, ICollection<ApiFieldError> errors)
    {
        if (password.Length < _passwordPolicy.MinimumLength) AddError(errors, "password", $"Password must be at least {_passwordPolicy.MinimumLength} characters.");
        if (password.Length > _passwordPolicy.MaximumLength) AddError(errors, "password", $"Password must not exceed {_passwordPolicy.MaximumLength} characters.");
        if (_passwordPolicy.RequireUppercase && !password.Any(char.IsUpper)) AddError(errors, "password", "Password must contain an uppercase letter.");
        if (_passwordPolicy.RequireLowercase && !password.Any(char.IsLower)) AddError(errors, "password", "Password must contain a lowercase letter.");
        if (_passwordPolicy.RequireDigit && !password.Any(char.IsDigit)) AddError(errors, "password", "Password must contain a digit.");
        if (_passwordPolicy.RequireSpecialCharacter && !password.Any(character => !char.IsLetterOrDigit(character))) AddError(errors, "password", "Password must contain a special character.");
    }

    private static async Task EnsureNoDuplicateUserAsync(AuthDbContext dbContext, string email, UserRole role, string? registrationNumber, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken)) throw Conflict("An account with this email already exists.");
        if (role == UserRole.Student && registrationNumber is not null
            && await dbContext.Users.AnyAsync(user => user.RegistrationNumber == registrationNumber, cancellationToken))
            throw Conflict("An account with this registration number already exists.");
    }

    private static void AddRequiredOrLengthError(ICollection<ApiFieldError> errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) AddError(errors, field, $"{ToDisplayName(field)} is required.");
        else if (value.Length > maxLength) AddError(errors, field, $"{ToDisplayName(field)} must not exceed {maxLength} characters.");
    }

    private static void AddError(ICollection<ApiFieldError> errors, string field, string message) => errors.Add(new ApiFieldError(field, new[] { message }));
    private static ApiValidationException Validation(string field, string message) => new(new List<ApiFieldError> { new(field, new[] { message }) });
    private static ApiException Conflict(string message, Exception? inner = null) => new(StatusCodes.Status409Conflict, ErrorCodes.Conflict, message, innerException: inner);
    private static string? NormalizeRegistrationNumber(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    private static string? ExtractEmailLocalPart(string email) { var at = email.IndexOf('@'); return at <= 0 ? null : email[..at].Trim().ToUpperInvariant(); }
    private static string ToApiRole(UserRole role) => role == UserRole.Student ? "STUDENT" : "SUPERVISOR";
    private static string GenerateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Sha256Base64(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static bool HashesEqual(string left, string right) => CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(left), Convert.FromBase64String(right));
    private static string ToDisplayName(string field) => field switch { "firstName" => "First name", "lastName" => "Last name", "email" => "Email", "fname" => "First name", "lname" => "Last name", "registrationToken" => "Registration token", _ => field };

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
