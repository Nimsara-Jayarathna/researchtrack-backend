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
        if (!string.Equals(source.ConnectionStatus, GitHubConnectionStatuses.Connected, StringComparison.Ordinal))
        {
            throw Conflict("The GitHub App installation is not currently connected.");
        }
        if (!GitHubAccessTypes.IsGitHubAppBacked(source.AccessType))
        {
            throw Conflict("The access source is not backed by the ResearchTrack GitHub App.");
        }

        var requestedIds = repositories
            .Select(item => item.GitHubRepositoryId)
            .ToArray();

        // MySql.EntityFrameworkCore 10 can fail while parameterizing a multi-value
        // Guid.Contains(...) predicate inside this serializable transaction. The
        // installation inventory is already scoped by source and is intentionally
        // small enough to materialize safely, so load the source inventory with a
        // simple translatable predicate and resolve the requested ids in memory.
        // This also preserves the request order for deterministic primary fallback.
        var sourceRepositories = await dbContext.Repositories
            .AsNoTracking()
            .Where(repository => repository.SourceId == sourceId)
            .ToListAsync(cancellationToken);
        var sourceRepositoriesById = sourceRepositories.ToDictionary(repository => repository.Id);
        var selectedRepositories = new List<GitHubRepository>(requestedIds.Length);
        foreach (var requestedId in requestedIds)
        {
            if (!sourceRepositoriesById.TryGetValue(requestedId, out var repository)
                || !repository.Available)
            {
                throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.NotFound,
                    "One or more selected repositories are no longer available to this GitHub App installation.");
            }

            selectedRepositories.Add(repository);
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
        var access = await (
            from source in db.AccessSources.AsNoTracking()
            join repository in db.Repositories.AsNoTracking() on source.Id equals repository.SourceId
            where source.Id == link.SourceId && repository.Id == link.GitHubRepositoryId
            select new { source.Active, source.ConnectionStatus, repository.Available })
            .SingleOrDefaultAsync(cancellationToken);
        if (access is null
            || !access.Active
            || !string.Equals(access.ConnectionStatus, GitHubConnectionStatuses.Connected, StringComparison.Ordinal))
        {
            throw Conflict("The GitHub App installation is not currently connected.");
        }
        if (!access.Available)
        {
            throw Conflict("The GitHub App no longer has access to this repository.");
        }

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
    var access = await (
        from source in db.AccessSources.AsNoTracking()
        join repository in db.Repositories.AsNoTracking() on source.Id equals repository.SourceId
        where source.Id == selected.SourceId && repository.Id == selected.GitHubRepositoryId
        select new { source.Active, source.ConnectionStatus, repository.Available })
        .SingleOrDefaultAsync(cancellationToken);
    if (access is null
        || !access.Active
        || !string.Equals(access.ConnectionStatus, GitHubConnectionStatuses.Connected, StringComparison.Ordinal)
        || !access.Available)
    {
        throw Conflict("A repository without active GitHub App access cannot be selected as primary.");
    }
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
    var state = await (
        from link in db.ProjectRepositoryLinks.AsNoTracking()
        join source in db.AccessSources.AsNoTracking() on link.SourceId equals source.Id
        join repository in db.Repositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
        where link.Id == linkedRepositoryId
            && link.ProjectId == projectId
            && link.Active
        select new
        {
            link.Enabled,
            link.SyncStatus,
            SourceActive = source.Active,
            source.ConnectionStatus,
            RepositoryAvailable = repository.Available
        }).SingleOrDefaultAsync(cancellationToken)
        ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The linked GitHub repository was not found for this project.");

    if (!state.Enabled)
    {
        throw Conflict("A disabled repository cannot be synchronized.");
    }
    if (!state.SourceActive || !string.Equals(state.ConnectionStatus, GitHubConnectionStatuses.Connected, StringComparison.Ordinal))
    {
        throw Conflict("The GitHub App installation is not currently connected.");
    }
    if (!state.RepositoryAvailable)
    {
        throw Conflict("The GitHub App no longer has access to this repository.");
    }
    if (string.Equals(state.SyncStatus, GitHubSyncStatuses.InProgress, StringComparison.Ordinal)
        || string.Equals(state.SyncStatus, GitHubSyncStatuses.Pending, StringComparison.Ordinal))
    {
        throw Conflict("Repository synchronization is already queued or in progress.");
    }

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
    if (affected != 1)
    {
        throw Conflict("Repository synchronization state changed. Refresh and try again.");
    }
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
        var hasUnacknowledgedAccess = await dbContext.AccessRequests
            .AsNoTracking()
            .AnyAsync(request =>
                request.ProjectId == projectId
                && request.Status == GitHubAccessRequestStatuses.Completed
                && request.SourceId != null
                && request.InstallationId != null
                && request.AcknowledgedAt == null
                && dbContext.AccessSources.Any(source =>
                    source.Id == request.SourceId && source.Active),
                cancellationToken);

        var sources = sourceEntities
            .Select(source => new GitHubAccessSourceResponse(
                source.Id,
                source.ProjectId,
                source.InstallationId,
                source.OwnerLogin,
                source.OwnerType,
                source.AccessType,
                source.ConnectionStatus,
                source.Active,
                AsUtc(source.CreatedAt)))
            .ToList();
        var linkRows = await (
            from link in dbContext.ProjectRepositoryLinks.AsNoTracking()
            join repository in dbContext.Repositories.AsNoTracking()
                on link.GitHubRepositoryId equals repository.Id
            join source in dbContext.AccessSources.AsNoTracking()
                on link.SourceId equals source.Id
            where link.ProjectId == projectId && link.Active
            orderby link.Primary descending, link.LinkedAt
            select new
            {
                Link = link,
                RepositoryAvailable = repository.Available,
                SourceConnectionStatus = source.ConnectionStatus
            }).ToListAsync(cancellationToken);
        var links = linkRows
            .Select(row => new ProjectRepositoryLinkResponse(
                row.Link.Id,
                row.Link.SourceId,
                row.Link.AccessType,
                row.Link.GitHubRepositoryId,
                row.Link.GitHubRepoId,
                row.Link.FullName,
                row.Link.Name,
                row.Link.CustomName,
                row.Link.OwnerLogin,
                row.Link.DefaultBranch,
                row.Link.Url,
                row.Link.Primary,
                row.Link.Enabled,
                AsUtc(row.Link.LinkedAt),
                row.Link.LastSyncedAt.HasValue ? AsUtc(row.Link.LastSyncedAt.Value) : null,
                row.Link.SyncRevision,
                row.Link.SyncStatus,
                ResolveAccessStatus(row.SourceConnectionStatus, row.RepositoryAvailable)))
            .ToList();

        return new ProjectGitHubRepositoriesResponse(
            projectId,
            _options.MaxLinkedRepositories,
            _options.MaxEnabledRepositories,
            hasUnacknowledgedAccess,
            sources,
            links);
    }

    private static string ResolveAccessStatus(string connectionStatus, bool repositoryAvailable) =>
        connectionStatus switch
        {
            GitHubConnectionStatuses.Suspended => GitHubRepositoryAccessStatuses.InstallationSuspended,
            GitHubConnectionStatuses.Removed => GitHubRepositoryAccessStatuses.InstallationRemoved,
            _ when !repositoryAvailable => GitHubRepositoryAccessStatuses.RepositoryAccessRevoked,
            _ => GitHubRepositoryAccessStatuses.Available
        };

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
