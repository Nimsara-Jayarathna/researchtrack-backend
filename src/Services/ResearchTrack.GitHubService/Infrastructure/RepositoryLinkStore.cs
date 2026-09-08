using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class RepositoryLinkStore : IRepositoryLinkStore
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly GitHubRepositoryLinkOptions _options;

    public RepositoryLinkStore(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        GitHubRepositoryLinkOptions options)
    {
        _dbContextFactory = dbContextFactory;
        _options = options;
    }

    public async Task<RepositoryLinkPersistenceResult> CreateLinksAsync(
        Guid projectId,
        Guid sourceId,
        Guid userId,
        IReadOnlyList<LinkGitHubRepositoryRequestItem> repositories,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var source = await dbContext.AccessSources
            .SingleOrDefaultAsync(item => item.Id == sourceId, cancellationToken)
            ?? throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The GitHub access source was not found.");

        if (source.ProjectId != projectId)
        {
            throw Conflict("The GitHub access source does not belong to this project.");
        }
        if (!source.Active)
        {
            throw Conflict("The GitHub access source is inactive.");
        }
        if (!string.Equals(source.AccessType, GitHubAccessTypes.PublicUrl, StringComparison.Ordinal))
        {
            throw Conflict("The access source type is not supported by this linking flow.");
        }

        var requestedIds = repositories.Select(item => item.GitHubRepositoryId).ToArray();
        var sourceRepositories = await dbContext.Repositories
            .Where(repository => repository.SourceId == sourceId)
            .ToListAsync(cancellationToken);
        if (sourceRepositories.Count != 1
            || requestedIds.Length != 1
            || sourceRepositories[0].Id != requestedIds[0])
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The selected repository was not validated for this public access source.");
        }

        var repository = sourceRepositories[0];
        var existingLinks = await dbContext.ProjectRepositoryLinks
            .Where(link => link.ProjectId == projectId && link.Active)
            .ToListAsync(cancellationToken);

        if (existingLinks.Any(link => link.GitHubRepoId == repository.GitHubRepositoryId))
        {
            throw Conflict("This GitHub repository is already linked to the project.");
        }
        if (existingLinks.Count + 1 > _options.MaxLinkedRepositories)
        {
            throw Conflict("The project has reached its linked repository limit.");
        }
        if (existingLinks.Count(link => link.Enabled) + 1 > _options.MaxEnabledRepositories)
        {
            throw Conflict("The project has reached its enabled repository limit.");
        }

        var selection = repositories[0];
        var makePrimary = selection.Primary == true || existingLinks.All(link => !link.Primary);
        if (makePrimary)
        {
            foreach (var currentPrimary in existingLinks.Where(link => link.Primary))
            {
                currentPrimary.Primary = false;
                currentPrimary.PrimaryProjectKey = null;
                currentPrimary.UpdatedAt = now;
            }

            if (dbContext.ChangeTracker.HasChanges())
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var link = new ProjectRepositoryLink
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SourceId = source.Id,
            GitHubRepositoryId = repository.Id,
            GitHubRepoId = repository.GitHubRepositoryId,
            LinkedByUserId = userId,
            AccessType = source.AccessType,
            FullName = repository.FullName,
            Name = repository.Name,
            CustomName = NormalizeCustomName(selection.CustomName),
            OwnerLogin = repository.OwnerLogin,
            DefaultBranch = repository.DefaultBranch,
            Url = repository.Url,
            Active = true,
            Primary = makePrimary,
            Enabled = true,
            ActiveRepositoryKey = $"{projectId:N}:{repository.GitHubRepositoryId}",
            PrimaryProjectKey = makePrimary ? projectId.ToString("N") : null,
            LinkedAt = now,
            LastSyncedAt = null,
            SyncStatus = GitHubSyncStatuses.Pending,
            UpdatedAt = now
        };

        dbContext.ProjectRepositoryLinks.Add(link);
        ProjectGitHubRepositoriesResponse response;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            response = await LoadProjectAsync(dbContext, projectId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw Conflict("This GitHub repository is already linked to the project.", exception);
        }

        return new RepositoryLinkPersistenceResult(
            response,
            [new InitialRepositorySyncRequest(
                projectId,
                link.Id,
                link.SourceId,
                link.GitHubRepositoryId,
                link.GitHubRepoId)]);
    }

    public async Task MarkSyncFailedAsync(
        Guid linkedRepositoryId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var link = await dbContext.ProjectRepositoryLinks.SingleOrDefaultAsync(
            item => item.Id == linkedRepositoryId && item.Active,
            cancellationToken);
        if (link is null || !string.Equals(link.SyncStatus, GitHubSyncStatuses.Pending, StringComparison.Ordinal))
        {
            return;
        }

        link.SyncStatus = GitHubSyncStatuses.Failed;
        link.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectGitHubRepositoriesResponse> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadProjectAsync(dbContext, projectId, cancellationToken);
    }

    private async Task<ProjectGitHubRepositoriesResponse> LoadProjectAsync(
        GitHubDbContext dbContext,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var sourceEntities = await dbContext.AccessSources
            .AsNoTracking()
            .Where(source => source.ProjectId == projectId && source.Active)
            .OrderBy(source => source.CreatedAt)
            .ToListAsync(cancellationToken);
        var sources = sourceEntities
            .Select(source => new GitHubAccessSourceResponse(
                source.Id,
                source.ProjectId,
                source.InstallationId,
                source.OwnerLogin,
                source.OwnerType,
                source.AccessType,
                source.Active,
                AsUtc(source.CreatedAt)))
            .ToList();
        var linkEntities = await dbContext.ProjectRepositoryLinks
            .AsNoTracking()
            .Where(link => link.ProjectId == projectId && link.Active)
            .OrderByDescending(link => link.Primary)
            .ThenBy(link => link.LinkedAt)
            .ToListAsync(cancellationToken);
        var links = linkEntities
            .Select(link => new ProjectRepositoryLinkResponse(
                link.Id,
                link.SourceId,
                link.AccessType,
                link.GitHubRepositoryId,
                link.GitHubRepoId,
                link.FullName,
                link.Name,
                link.CustomName,
                link.OwnerLogin,
                link.DefaultBranch,
                link.Url,
                link.Primary,
                link.Enabled,
                AsUtc(link.LinkedAt),
                link.LastSyncedAt.HasValue ? AsUtc(link.LastSyncedAt.Value) : null,
                link.SyncStatus))
            .ToList();

        return new ProjectGitHubRepositoriesResponse(
            projectId,
            _options.MaxLinkedRepositories,
            _options.MaxEnabledRepositories,
            sources,
            links);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? NormalizeCustomName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static ApiException Conflict(string message, Exception? inner = null) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.Conflict,
        message,
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
