using System.Data;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
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
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

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
        if (!GitHubAccessTypes.IsGitHubAppBacked(source.AccessType))
        {
            throw Conflict("The access source is not backed by the ResearchTrack GitHub App.");
        }

        var requestedIds = repositories
            .Select(item => item.GitHubRepositoryId)
            .ToArray();

        var selectedRepositories = await dbContext.Repositories
            .Where(repository => repository.SourceId == sourceId
                && requestedIds.Contains(repository.Id))
            .ToListAsync(cancellationToken);
        if (selectedRepositories.Count != requestedIds.Length)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "One or more selected repositories were not verified for this GitHub App access source.");
        }

        var existingLinks = await dbContext.ProjectRepositoryLinks
            .Where(link => link.ProjectId == projectId && link.Active)
            .ToListAsync(cancellationToken);
        var selectedGitHubIds = selectedRepositories
            .Select(repository => repository.GitHubRepositoryId)
            .ToHashSet();

        if (existingLinks.Any(link => selectedGitHubIds.Contains(link.GitHubRepoId)))
        {
            throw Conflict("One or more selected GitHub repositories are already linked to the project.");
        }
        if (existingLinks.Count + selectedRepositories.Count > _options.MaxLinkedRepositories)
        {
            throw Conflict("The project has reached its linked repository limit.");
        }
        if (existingLinks.Count(link => link.Enabled) + selectedRepositories.Count
            > _options.MaxEnabledRepositories)
        {
            throw Conflict("The project has reached its enabled repository limit.");
        }

        var selections = repositories.ToDictionary(item => item.GitHubRepositoryId);
        var explicitPrimaryId = repositories
            .Where(item => item.Primary == true)
            .Select(item => (Guid?)item.GitHubRepositoryId)
            .SingleOrDefault();
        var newPrimaryId = explicitPrimaryId
            ?? (existingLinks.All(link => !link.Primary)
                ? requestedIds[0]
                : (Guid?)null);

        if (newPrimaryId is not null)
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

        var links = selectedRepositories
            .Select(repository =>
            {
                var selection = selections[repository.Id];
                var makePrimary = repository.Id == newPrimaryId;
                return new ProjectRepositoryLink
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
                    PrimaryProjectKey = makePrimary ? projectId.ToString("N") : null,
                    LinkedAt = now,
                    LastSyncedAt = null,
                    SyncStatus = GitHubSyncStatuses.Pending,
                    UpdatedAt = now
                };
            })
            .ToList();

        dbContext.ProjectRepositoryLinks.AddRange(links);
        ProjectGitHubRepositoriesResponse response;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            response = await LoadProjectAsync(dbContext, projectId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw Conflict("One or more selected GitHub repositories are already linked to the project.", exception);
        }

        return new RepositoryLinkPersistenceResult(
            response,
            links.Select(link => new InitialRepositorySyncRequest(
                projectId,
                link.Id,
                link.SourceId,
                link.GitHubRepositoryId,
                link.GitHubRepoId)).ToList());
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


public async Task<Guid?> GetLinkProjectIdAsync(Guid linkedRepositoryId, CancellationToken cancellationToken)
{
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    return await db.ProjectRepositoryLinks.AsNoTracking()
        .Where(link => link.Id == linkedRepositoryId && link.Active)
        .Select(link => (Guid?)link.ProjectId)
        .SingleOrDefaultAsync(cancellationToken);
}

public async Task<Guid?> GetSourceProjectIdAsync(Guid sourceId, CancellationToken cancellationToken)
{
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    return await db.AccessSources.AsNoTracking()
        .Where(source => source.Id == sourceId && source.Active)
        .Select(source => (Guid?)source.ProjectId)
        .SingleOrDefaultAsync(cancellationToken);
}

public async Task<RepositoryEnablementPersistenceResult> SetEnabledAsync(
    Guid linkedRepositoryId, bool enabled, DateTime now, CancellationToken cancellationToken)
{
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    var link = await RequireActiveLinkAsync(db, linkedRepositoryId, cancellationToken);
    EnsureNotSyncing(link, enabled ? "enable" : "disable");
    if (link.Enabled == enabled)
    {
        var unchanged = await LoadProjectAsync(db, link.ProjectId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new RepositoryEnablementPersistenceResult(unchanged, false, enabled);
    }
    if (enabled)
    {
        var enabledCount = await db.ProjectRepositoryLinks.CountAsync(
            item => item.ProjectId == link.ProjectId && item.Active && item.Enabled,
            cancellationToken);
        if (enabledCount >= _options.MaxEnabledRepositories) throw Conflict("The project has reached its enabled repository limit.");
        link.Enabled = true;
        link.SyncStatus = GitHubSyncStatuses.Pending;
        var hasPrimary = await db.ProjectRepositoryLinks.AnyAsync(
            item => item.ProjectId == link.ProjectId && item.Active && item.Enabled && item.Primary,
            cancellationToken);
        if (!hasPrimary)
        {
            link.Primary = true;
            link.PrimaryProjectKey = link.ProjectId.ToString("N");
        }
    }
    else
    {
        var wasPrimary = link.Primary;
        link.Enabled = false;
        link.Primary = false;
        link.PrimaryProjectKey = null;
        link.SyncStatus = GitHubSyncStatuses.Disabled;
        if (wasPrimary) await PromotePrimaryAsync(db, link.ProjectId, link.Id, now, cancellationToken);
    }
    link.UpdatedAt = now;
    await db.SaveChangesAsync(cancellationToken);
    var response = await LoadProjectAsync(db, link.ProjectId, cancellationToken);
    await tx.CommitAsync(cancellationToken);
    return new RepositoryEnablementPersistenceResult(response, true, enabled);
}

public async Task<ProjectGitHubRepositoriesResponse> UnlinkAsync(
    Guid linkedRepositoryId, DateTime now, CancellationToken cancellationToken)
{
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    var link = await RequireActiveLinkAsync(db, linkedRepositoryId, cancellationToken);
    EnsureNotSyncing(link, "unlink");
    var projectId = link.ProjectId;
    var wasPrimary = link.Primary;
    link.Active = false;
    link.Enabled = false;
    link.Primary = false;
    link.PrimaryProjectKey = null;
    link.SyncStatus = GitHubSyncStatuses.Disabled;
    link.UpdatedAt = now;
    if (wasPrimary) await PromotePrimaryAsync(db, projectId, link.Id, now, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);
    var response = await LoadProjectAsync(db, projectId, cancellationToken);
    await tx.CommitAsync(cancellationToken);
    return response;
}

public async Task<ProjectGitHubRepositoriesResponse> SelectPrimaryAsync(
    Guid linkedRepositoryId, DateTime now, CancellationToken cancellationToken)
{
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    var selected = await RequireActiveLinkAsync(db, linkedRepositoryId, cancellationToken);
    if (!selected.Enabled) throw Conflict("A disabled repository cannot be selected as primary.");
    var current = await db.ProjectRepositoryLinks
        .Where(link => link.ProjectId == selected.ProjectId && link.Active && link.Primary && link.Id != selected.Id)
        .ToListAsync(cancellationToken);
    foreach (var link in current)
    {
        link.Primary = false;
        link.PrimaryProjectKey = null;
        link.UpdatedAt = now;
    }
    selected.Primary = true;
    selected.PrimaryProjectKey = selected.ProjectId.ToString("N");
    selected.UpdatedAt = now;
    await db.SaveChangesAsync(cancellationToken);
    var response = await LoadProjectAsync(db, selected.ProjectId, cancellationToken);
    await tx.CommitAsync(cancellationToken);
    return response;
}

public async Task<ProjectGitHubRepositoriesResponse> UpdateDisplayNameAsync(
    Guid linkedRepositoryId, string? customName, DateTime now, CancellationToken cancellationToken)
{
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var link = await RequireActiveLinkAsync(db, linkedRepositoryId, cancellationToken);
    link.CustomName = NormalizeCustomName(customName);
    link.UpdatedAt = now;
    await db.SaveChangesAsync(cancellationToken);
    return await LoadProjectAsync(db, link.ProjectId, cancellationToken);
}

public async Task<ProjectGitHubRepositoriesResponse> DisconnectSourceAsync(
    Guid sourceId, DateTime now, CancellationToken cancellationToken)
{
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    var source = await db.AccessSources.SingleOrDefaultAsync(item => item.Id == sourceId && item.Active, cancellationToken)
        ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The active GitHub access source was not found.");
    var links = await db.ProjectRepositoryLinks
        .Where(link => link.SourceId == sourceId && link.Active)
        .ToListAsync(cancellationToken);
    if (links.Any(link => string.Equals(link.SyncStatus, GitHubSyncStatuses.InProgress, StringComparison.Ordinal)))
    {
        throw Conflict("Cannot disconnect a GitHub access source while one of its repositories is synchronizing.");
    }
    foreach (var link in links)
    {
        link.Active = false;
        link.Enabled = false;
        link.Primary = false;
        link.PrimaryProjectKey = null;
        link.SyncStatus = GitHubSyncStatuses.Disabled;
        link.UpdatedAt = now;
    }
    source.Active = false;
    source.ActiveInstallationKey = null;
    source.UpdatedAt = now;
    await db.SaveChangesAsync(cancellationToken);
    await PromotePrimaryAsync(db, source.ProjectId, Guid.Empty, now, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);
    var response = await LoadProjectAsync(db, source.ProjectId, cancellationToken);
    await tx.CommitAsync(cancellationToken);
    return response;
}

public async Task PrepareManualSyncAsync(
    Guid projectId, Guid linkedRepositoryId, DateTime now, CancellationToken cancellationToken)
{
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var affected = await db.ProjectRepositoryLinks
        .Where(item => item.Id == linkedRepositoryId
            && item.ProjectId == projectId
            && item.Active
            && item.Enabled
            && item.SyncStatus != GitHubSyncStatuses.InProgress
            && item.SyncStatus != GitHubSyncStatuses.Pending)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.SyncStatus, GitHubSyncStatuses.Pending)
            .SetProperty(item => item.UpdatedAt, now), cancellationToken);
    if (affected == 1)
    {
        return;
    }

    var link = await db.ProjectRepositoryLinks.AsNoTracking().SingleOrDefaultAsync(
        item => item.Id == linkedRepositoryId && item.ProjectId == projectId && item.Active,
        cancellationToken) ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The linked GitHub repository was not found for this project.");
    if (!link.Enabled)
    {
        throw Conflict("A disabled repository cannot be synchronized.");
    }
    throw Conflict("Repository synchronization is already queued or in progress.");
}

private static async Task<ProjectRepositoryLink> RequireActiveLinkAsync(
    GitHubDbContext db, Guid linkedRepositoryId, CancellationToken cancellationToken) =>
    await db.ProjectRepositoryLinks.SingleOrDefaultAsync(link => link.Id == linkedRepositoryId && link.Active, cancellationToken)
    ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The linked GitHub repository was not found.");

private static void EnsureNotSyncing(ProjectRepositoryLink link, string operation)
{
    if (string.Equals(link.SyncStatus, GitHubSyncStatuses.InProgress, StringComparison.Ordinal))
    {
        throw Conflict($"Cannot {operation} repository while synchronization is in progress.");
    }
}

private static async Task PromotePrimaryAsync(
    GitHubDbContext db, Guid projectId, Guid excludedId, DateTime now, CancellationToken cancellationToken)
{
    var hasPrimary = await db.ProjectRepositoryLinks.AnyAsync(
        link => link.ProjectId == projectId
            && link.Active
            && link.Enabled
            && link.Primary
            && link.Id != excludedId,
        cancellationToken);
    if (hasPrimary) return;
    var replacement = await db.ProjectRepositoryLinks
        .Where(link => link.ProjectId == projectId && link.Active && link.Enabled && link.Id != excludedId)
        .OrderBy(link => link.LinkedAt)
        .ThenBy(link => link.Id)
        .FirstOrDefaultAsync(cancellationToken);
    if (replacement is null) return;
    replacement.Primary = true;
    replacement.PrimaryProjectKey = projectId.ToString("N");
    replacement.UpdatedAt = now;
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
