using Microsoft.EntityFrameworkCore;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Persistence;

namespace ResearchTrack.AuthService.Features.Users;

public sealed class UserDirectoryService : IUserDirectoryService
{
    private const int SearchResultLimit = 25;
    private readonly IDbContextFactory<AuthDbContext> _dbContextFactory;

    public UserDirectoryService(IDbContextFactory<AuthDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<UserDirectoryResponse>> SearchStudentsAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        var normalized = query?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 3)
        {
            return [];
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Role == UserRole.Student)
            .Where(user =>
                user.FirstName.Contains(normalized)
                || user.LastName.Contains(normalized)
                || user.Email.Contains(normalized)
                || (user.RegistrationNumber != null
                    && user.RegistrationNumber.Contains(normalized)))
            .OrderBy(user => user.FirstName)
            .ThenBy(user => user.LastName)
            .Take(SearchResultLimit)
            .Select(user => new UserDirectoryResponse(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.RegistrationNumber,
                user.Role == UserRole.Student ? "STUDENT" : "SUPERVISOR"))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserDirectoryResponse>> ResolveStudentsAsync(
        IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken)
    {
        if (studentIds.Count == 0)
        {
            return [];
        }

        var uniqueIds = studentIds.Distinct().ToArray();

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Users
            .AsNoTracking()
            .Where(user => uniqueIds.Contains(user.Id) && user.Role == UserRole.Student)
            .OrderBy(user => user.FirstName)
            .ThenBy(user => user.LastName)
            .Select(user => new UserDirectoryResponse(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.RegistrationNumber,
                user.Role == UserRole.Student ? "STUDENT" : "SUPERVISOR"))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserDirectoryResponse?> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserDirectoryResponse(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.RegistrationNumber,
                user.Role == UserRole.Student ? "STUDENT" : "SUPERVISOR"))
            .SingleOrDefaultAsync(cancellationToken);
    }

}
