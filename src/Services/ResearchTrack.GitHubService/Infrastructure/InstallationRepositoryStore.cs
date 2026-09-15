using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class InstallationRepositoryStore : IInstallationRepositoryStore
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;

    public InstallationRepositoryStore(IDbContextFactory<GitHubDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<InstallationAccessSourceSnapshot?> GetSourceAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ActiveGitHubAppSources(dbContext.AccessSources.AsNoTracking())
            .Where(source => source.Id == sourceId)
            .Select(source => new InstallationAccessSourceSnapshot(
                source.Id,
                source.ProjectId,
                source.InstallationId!.Value,
                source.OwnerLogin,
                source.OwnerType,
                source.AccessType))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<InstallationAccessSourceSnapshot?> GetSourceByInstallationAsync(
        Guid projectId,
        long installationId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ActiveGitHubAppSources(dbContext.AccessSources.AsNoTracking())
            .Where(source => source.ProjectId == projectId
                && source.InstallationId == installationId)
            .Select(source => new InstallationAccessSourceSnapshot(
                source.Id,
                source.ProjectId,
                source.InstallationId!.Value,
                source.OwnerLogin,
                source.OwnerType,
                source.AccessType))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<GitHubAvailableRepositoriesResponse> UpsertAvailableAsync(
        Guid sourceId,
        IReadOnlyList<GitHubInstallationRepository> repositories,
        DateTime now,
        CancellationToken cancellationToken) =>
        UpsertAvailableCoreAsync(sourceId, repositories, now, retryOnDuplicate: true, cancellationToken: cancellationToken);

    private async Task<GitHubAvailableRepositoriesResponse> UpsertAvailableCoreAsync(
        Guid sourceId,
        IReadOnlyList<GitHubInstallationRepository> repositories,
        DateTime now,
        bool retryOnDuplicate,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sourceExists = await ActiveGitHubAppSources(dbContext.AccessSources)
            .AnyAsync(source => source.Id == sourceId, cancellationToken);
        if (!sourceExists)
        {
            throw SourceNotFound();
        }

        var existing = await dbContext.Repositories
            .Where(repository => repository.SourceId == sourceId)
            .ToDictionaryAsync(repository => repository.GitHubRepositoryId, cancellationToken);

        var selectedEntities = new List<GitHubRepository>(repositories.Count);
        foreach (var repository in repositories)
        {
            if (!existing.TryGetValue(repository.Id, out var entity))
            {
                entity = new GitHubRepository
                {
                    Id = Guid.NewGuid(),
                    SourceId = sourceId,
                    GitHubRepositoryId = repository.Id,
                    FullName = repository.FullName,
                    Name = repository.Name,
                    OwnerLogin = repository.OwnerLogin,
                    DefaultBranch = repository.DefaultBranch,
                    Url = repository.HtmlUrl,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                dbContext.Repositories.Add(entity);
                existing[repository.Id] = entity;
            }
            else
            {
                ApplyMetadata(entity, repository, now);
            }

            selectedEntities.Add(entity);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (retryOnDuplicate && IsDuplicateKey(exception))
        {
            // The access-updated page can legitimately request the same inventory
            // twice (for example React development StrictMode), and production
            // instances can also refresh concurrently. Treat the unique
            // source/repository key as an idempotency boundary and retry once
            // against the rows committed by the competing request.
            return await UpsertAvailableCoreAsync(
                sourceId,
                repositories,
                now,
                retryOnDuplicate: false,
                cancellationToken: cancellationToken);
        }

        var items = selectedEntities
            .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();

        return new GitHubAvailableRepositoriesResponse(sourceId, items, items.Count);
    }

    public async Task<InstallationRepositorySelection?> GetSelectionAsync(
        Guid sourceId,
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Repositories
            .AsNoTracking()
            .Where(repository => repository.SourceId == sourceId && repository.Id == repositoryId)
            .Select(repository => new InstallationRepositorySelection(
                repository.Id,
                repository.GitHubRepositoryId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid> UpsertVerifiedAsync(
        Guid sourceId,
        GitHubInstallationRepository repository,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Repositories.SingleOrDefaultAsync(
            item => item.SourceId == sourceId && item.GitHubRepositoryId == repository.Id,
            cancellationToken);

        if (entity is null)
        {
            entity = new GitHubRepository
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                GitHubRepositoryId = repository.Id,
                FullName = repository.FullName,
                Name = repository.Name,
                OwnerLogin = repository.OwnerLogin,
                DefaultBranch = repository.DefaultBranch,
                Url = repository.HtmlUrl,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.Repositories.Add(entity);
        }
        else
        {
            ApplyMetadata(entity, repository, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }


    private static IQueryable<GitHubAccessSource> ActiveGitHubAppSources(
        IQueryable<GitHubAccessSource> sources) =>
        sources.Where(source =>
            source.Active
            && source.InstallationId != null
            && (source.AccessType == GitHubAccessTypes.InstallationDirect
                || source.AccessType == GitHubAccessTypes.InstallationRequested));

    private static void ApplyMetadata(
        GitHubRepository entity,
        GitHubInstallationRepository repository,
        DateTime now)
    {
        entity.FullName = repository.FullName;
        entity.Name = repository.Name;
        entity.OwnerLogin = repository.OwnerLogin;
        entity.DefaultBranch = repository.DefaultBranch;
        entity.Url = repository.HtmlUrl;
        entity.UpdatedAt = now;
    }

    private static GitHubRepositoryOptionResponse ToResponse(GitHubRepository repository) => new(
        repository.Id,
        repository.GitHubRepositoryId,
        repository.FullName,
        repository.Name,
        repository.OwnerLogin,
        repository.DefaultBranch,
        repository.Url);

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

    private static ApiException SourceNotFound() => new(
        StatusCodes.Status404NotFound,
        ErrorCodes.NotFound,
        "The active GitHub App access source was not found.");
}
