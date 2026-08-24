using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Infrastructure.Tokens;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.AuthService.Features.Authentication;

public sealed class UserAuthenticationService : IUserAuthenticationService
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    private const string InvalidSessionMessage = "Authentication session is invalid or has expired.";

    private readonly IDbContextFactory<AuthDbContext> _dbContextFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly InvalidPasswordTimingGuard _timingGuard;
    private readonly IAccessTokenService _accessTokenService;
    private readonly JwtOptions _jwtOptions;

    public UserAuthenticationService(
        IDbContextFactory<AuthDbContext> dbContextFactory,
        IPasswordHasher passwordHasher,
        InvalidPasswordTimingGuard timingGuard,
        IAccessTokenService accessTokenService,
        JwtOptions jwtOptions)
    {
        _dbContextFactory = dbContextFactory;
        _passwordHasher = passwordHasher;
        _timingGuard = timingGuard;
        _accessTokenService = accessTokenService;
        _jwtOptions = jwtOptions;
    }

    public async Task<AuthSessionResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = request.Email!.Trim().ToLowerInvariant();
        var password = request.Password!;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            _timingGuard.Verify(password);
            throw Unauthorized(InvalidCredentialsMessage);
        }

        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            throw Unauthorized(InvalidCredentialsMessage);
        }

        var rawRefreshToken = GenerateRawRefreshToken();
        dbContext.RefreshTokens.Add(CreateRefreshToken(user.Id, rawRefreshToken, DateTime.UtcNow));
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateSessionResult(user, rawRefreshToken);
    }

    public async Task<AuthSessionResult> RefreshAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            throw Unauthorized(InvalidSessionMessage);
        }

        var tokenHash = HashRefreshToken(rawRefreshToken);
        var now = DateTime.UtcNow;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentToken = await dbContext.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (currentToken is null
            || currentToken.RevokedAt is not null
            || currentToken.ExpiresAt <= now)
        {
            throw Unauthorized(InvalidSessionMessage);
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == currentToken.UserId, cancellationToken);

        if (user is null)
        {
            throw Unauthorized(InvalidSessionMessage);
        }

        // Atomic claim prevents two concurrent refresh requests from successfully
        // rotating the same one-time token.
        var revoked = await dbContext.RefreshTokens
            .Where(token => token.Id == currentToken.Id
                && token.RevokedAt == null
                && token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(token => token.RevokedAt, now),
                cancellationToken);

        if (revoked != 1)
        {
            throw Unauthorized(InvalidSessionMessage);
        }

        var replacementRawToken = GenerateRawRefreshToken();
        dbContext.RefreshTokens.Add(CreateRefreshToken(user.Id, replacementRawToken, now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateSessionResult(user, replacementRawToken);
    }

    public async Task RevokeRefreshTokenAsync(
        string? rawRefreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return;
        }

        var tokenHash = HashRefreshToken(rawRefreshToken);
        var now = DateTime.UtcNow;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.RefreshTokens
            .Where(token => token.TokenHash == tokenHash && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(token => token.RevokedAt, now),
                cancellationToken);
    }

    public async Task<LoginResponse> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var subject = principal.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        if (!Guid.TryParse(subject, out var userId))
        {
            throw Unauthorized(InvalidSessionMessage);
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            throw Unauthorized(InvalidSessionMessage);
        }

        return new LoginResponse(ToResponse(user));
    }

    private AuthSessionResult CreateSessionResult(User user, string rawRefreshToken) => new(
        _accessTokenService.Generate(user),
        rawRefreshToken,
        new LoginResponse(ToResponse(user)));

    private RefreshToken CreateRefreshToken(Guid userId, string rawToken, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = HashRefreshToken(rawToken),
        ExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays),
        CreatedAt = now
    };

    private static AuthUserResponse ToResponse(User user) => new(
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        user.Role == UserRole.Student
            ? AuthSecurityConstants.Roles.Student
            : AuthSecurityConstants.Roles.Supervisor);

    private static string GenerateRawRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashRefreshToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(bytes);
    }

    private static ApiException Unauthorized(string message) => new(
        StatusCodes.Status401Unauthorized,
        ErrorCodes.Unauthorized,
        message);
}
