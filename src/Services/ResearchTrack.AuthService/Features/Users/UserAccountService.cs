using Microsoft.EntityFrameworkCore;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Features.Passwords;
using ResearchTrack.AuthService.Infrastructure.Security;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.AuthService.Features.Users;

public sealed class UserAccountService : IUserAccountService
{
    private const string IncorrectCurrentPasswordMessage = "Current password is incorrect.";

    private readonly IDbContextFactory<AuthDbContext> _dbContextFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPasswordPolicyValidator _passwordPolicyValidator;

    public UserAccountService(
        IDbContextFactory<AuthDbContext> dbContextFactory,
        IPasswordHasher passwordHasher,
        IPasswordPolicyValidator passwordPolicyValidator)
    {
        _dbContextFactory = dbContextFactory;
        _passwordHasher = passwordHasher;
        _passwordPolicyValidator = passwordPolicyValidator;
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currentPassword = request.CurrentPassword;
        var newPassword = request.NewPassword;
        var validationErrors = new List<ApiFieldError>();

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            validationErrors.Add(new ApiFieldError(
                "currentPassword",
                ["Current password is required."]));
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            validationErrors.Add(new ApiFieldError(
                "newPassword",
                ["New password is required."]));
        }
        else
        {
            validationErrors.AddRange(
                _passwordPolicyValidator.Validate(newPassword, "newPassword"));
        }

        if (validationErrors.Count > 0)
        {
            throw new ApiValidationException(validationErrors);
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken);

        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthorized,
                "Authentication is required.");
        }

        if (!_passwordHasher.Verify(currentPassword!, user.PasswordHash))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.CurrentPasswordIncorrect,
                IncorrectCurrentPasswordMessage);
        }

        if (_passwordHasher.Verify(newPassword!, user.PasswordHash))
        {
            throw new ApiValidationException(
                [new ApiFieldError(
                    "newPassword",
                    ["New password must be different from current password."])]);
        }

        var now = DateTime.UtcNow;
        user.PasswordHash = _passwordHasher.Hash(newPassword!);
        user.UpdatedAt = now;

        await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(token => token.RevokedAt, now),
                cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
