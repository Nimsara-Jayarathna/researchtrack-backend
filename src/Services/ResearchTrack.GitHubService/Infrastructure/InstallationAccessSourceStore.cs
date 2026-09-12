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
        DateTime now,
        CancellationToken cancellationToken)
    {
        var activeInstallationKey = $"{projectId:N}:{GitHubAccessTypes.GitHubApp}:{installation.InstallationId}";
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.AccessSources.SingleOrDefaultAsync(
            source => source.ActiveInstallationKey == activeInstallationKey,
            cancellationToken);
        if (existing is not null)
        {
            existing.OwnerLogin = installation.OwnerLogin;
            existing.OwnerType = installation.OwnerType;
            existing.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var source = new GitHubAccessSource
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            CreatedByUserId = userId,
            InstallationId = installation.InstallationId,
            OwnerLogin = installation.OwnerLogin,
            OwnerType = installation.OwnerType,
            AccessType = GitHubAccessTypes.GitHubApp,
            Active = true,
            ActiveRepositoryKey = null,
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
            throw DuplicateInstallationSource(exception);
        }

        return source.Id;
    }

    private static ApiException DuplicateInstallationSource(Exception? inner = null) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.Conflict,
        "An active GitHub App installation source already exists for this project.",
        innerException: inner);

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
