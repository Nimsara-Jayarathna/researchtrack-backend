using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class InstallationAccessSourceStore : IInstallationAccessSourceStore
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;

    public InstallationAccessSourceStore(IDbContextFactory<GitHubDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<Guid> CreateAsync(
        Guid projectId,
        Guid userId,
        GitHubInstallationInfo installation,
        string accessType,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!GitHubAccessTypes.IsGitHubAppBacked(accessType))
        {
            throw new ArgumentOutOfRangeException(nameof(accessType));
        }
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.AccessSources.SingleOrDefaultAsync(
            source => source.ProjectId == projectId
                && source.InstallationId == installation.InstallationId
                && source.Active,
            cancellationToken);
        if (existing is not null)
        {
            existing.OwnerLogin = installation.OwnerLogin;
            existing.OwnerType = installation.OwnerType;
            existing.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var activeInstallationKey = $"{projectId:N}:GITHUB_APP:{installation.InstallationId}";
        var source = new GitHubAccessSource
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            CreatedByUserId = userId,
            InstallationId = installation.InstallationId,
            OwnerLogin = installation.OwnerLogin,
            OwnerType = installation.OwnerType,
            AccessType = accessType,
            Active = true,
            ActiveInstallationKey = activeInstallationKey,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.AccessSources.Add(source);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            // Another callback may have completed the same installation between
            // our initial read and insert. Treat that race as idempotent.
            dbContext.ChangeTracker.Clear();
            var raced = await dbContext.AccessSources.AsNoTracking().SingleOrDefaultAsync(
                item => item.ProjectId == projectId
                    && item.InstallationId == installation.InstallationId
                    && item.Active,
                cancellationToken);
            if (raced is not null)
            {
                return raced.Id;
            }

            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "An active GitHub App installation source already exists for this project.",
                innerException: exception);
        }
        return source.Id;
    }

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
