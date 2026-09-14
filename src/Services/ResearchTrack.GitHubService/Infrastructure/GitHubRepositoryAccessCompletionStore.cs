using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class GitHubRepositoryAccessCompletionStore : IGitHubRepositoryAccessCompletionStore
{
    private const int MaxConcurrencyAttempts = 2;

    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly GitHubRepositoryLinkOptions _options;
    private readonly TimeProvider _timeProvider;

    public GitHubRepositoryAccessCompletionStore(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        GitHubRepositoryLinkOptions options,
        TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<OwnerGrantedRepositoryCompletionPersistenceResult> CompleteAsync(
        Guid requestId,
        GitHubInstallationInfo installation,
        GitHubInstallationRepository repository,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }
        if (installation.InstallationId <= 0 || repository.Id <= 0)
        {
            throw new ArgumentException("Verified GitHub installation and repository identities are required.");
        }

        Guid projectId;
        await using (var lookup = await _dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            projectId = await lookup.RepositoryAccessRequests
                .AsNoTracking()
                .Where(request => request.Id == requestId)
                .Select(request => request.ProjectId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (projectId == Guid.Empty)
        {
            return Missing(requestId);
        }

        // The named project lock is shared by the normal repository-link path and direct
        // installation-source creation. It serializes project capacity checks and inserts.
        await using var lockContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var projectLock = await ProjectRepositoryMutationLock.AcquireAsync(
            lockContext,
            projectId,
            cancellationToken);

        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                return await CompleteOnceAsync(
                    requestId,
                    projectId,
                    installation,
                    repository,
                    now,
                    cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A terminal request transition can race the callback even while repository
                // mutations are project-serialized. Retry from fresh state when possible.
                if (attempt == MaxConcurrencyAttempts)
                {
                    throw CompletionInProgress();
                }
            }
            catch (DbUpdateException exception) when (IsDuplicateKey(exception))
            {
                // Database uniqueness constraints are the final defense against a legacy writer
                // that does not yet participate in the project mutation lock. Retry from fresh state.
                // If the collision cannot be resolved after a bounded retry, leave the owner request
                // retryable rather than consuming a still-PENDING request with an ambiguous result.
                if (attempt == MaxConcurrencyAttempts)
                {
                    throw CompletionInProgress(exception);
                }
            }
        }

        throw CompletionInProgress();
    }

    private async Task<OwnerGrantedRepositoryCompletionPersistenceResult> CompleteOnceAsync(
        Guid requestId,
        Guid projectId,
        GitHubInstallationInfo installation,
        GitHubInstallationRepository repository,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var request = await db.RepositoryAccessRequests
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Missing(requestId);
        }

        // The project lock and database reads can outlive the bearer deadline.
        // Never authorize completion using only the time captured by the caller.
        now = Later(now, _timeProvider.GetUtcNow().UtcDateTime);

        if (request.ProjectId != projectId)
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.RequestContextMismatch,
                now,
                cancellationToken);
        }

        if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Completed, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);

            // Idempotency applies only to the exact already-completed identity. A caller cannot
            // replay a completed request with a different installation or repository and have it
            // treated as a successful completion.
            if (request.PendingInstallationId != installation.InstallationId
                || request.GitHubRepositoryId != repository.Id
                || !string.Equals(request.RequestedOwner, repository.OwnerLogin, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(request.RequestedRepositoryName, repository.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Failed(request, GitHubRepositoryAccessFailureCodes.CompletionInconsistent);
            }

            return await ResolveCompletedAsync(db, request, cancellationToken);
        }

        if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Failed, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failed(
                request,
                GitHubRepositoryAccessFailureCodes.IsSafe(request.FailureCode)
                    ? request.FailureCode!
                    : GitHubRepositoryAccessFailureCodes.RequestFailed);
        }

        if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failed(request, GitHubRepositoryAccessFailureCodes.RequestExpired);
        }

        if (!string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal)
            || !string.Equals(request.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.RequestNotPending,
                now,
                cancellationToken);
        }

        if (request.ExpiresAt <= now)
        {
            request.Status = GitHubRepositoryAccessRequestStatuses.Expired;
            request.FailureCode = null;
            request.ConsumedAt = now;
            request.Version++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Failed(request, GitHubRepositoryAccessFailureCodes.RequestExpired);
        }

        if (request.PendingInstallationId != installation.InstallationId)
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.InstallationMismatch,
                now,
                cancellationToken);
        }

        if (!string.Equals(request.RequestedOwner, repository.OwnerLogin, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.RequestedRepositoryName, repository.Name, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                request.RequestedFullName,
                $"{repository.OwnerLogin}/{repository.Name}",
                StringComparison.OrdinalIgnoreCase))
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.RepositoryMismatch,
                now,
                cancellationToken);
        }

        if (request.GitHubRepositoryId is long previouslyVerifiedId
            && previouslyVerifiedId != repository.Id)
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.RepositoryIdentityChanged,
                now,
                cancellationToken);
        }

        var existingLinks = await db.ProjectRepositoryLinks
            .Where(link => link.ProjectId == projectId && link.Active)
            .ToListAsync(cancellationToken);
        if (existingLinks.Any(link => link.GitHubRepoId == repository.Id))
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.RepositoryAlreadyLinked,
                now,
                cancellationToken);
        }
        if (existingLinks.Count + 1 > _options.MaxLinkedRepositories)
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.ProjectRepositoryLimitReached,
                now,
                cancellationToken);
        }
        if (existingLinks.Count(link => link.Enabled) + 1 > _options.MaxEnabledRepositories)
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.ProjectEnabledRepositoryLimitReached,
                now,
                cancellationToken);
        }

        var installationSources = await db.AccessSources
            .Where(source => source.ProjectId == projectId
                && source.Active
                && source.InstallationId == installation.InstallationId)
            .ToListAsync(cancellationToken);
        if (installationSources.Count > 1
            || (installationSources.Count == 1
                && !GitHubAccessTypes.IsInstallationBacked(installationSources[0].AccessType)))
        {
            return await FailAndCommitAsync(
                db,
                transaction,
                request,
                GitHubRepositoryAccessFailureCodes.InstallationSourceConflict,
                now,
                cancellationToken);
        }

        var source = installationSources.SingleOrDefault();
        if (source is null)
        {
            source = new GitHubAccessSource
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                CreatedByUserId = request.InitiatingUserId,
                InstallationId = installation.InstallationId,
                OwnerLogin = installation.OwnerLogin,
                OwnerType = installation.OwnerType,
                AccessType = GitHubAccessTypes.InstallationRequested,
                Active = true,
                ActiveRepositoryKey = null,
                ActiveInstallationKey = GitHubAccessTypes.BuildActiveInstallationKey(
                    projectId,
                    installation.InstallationId),
                CreatedAt = now,
                UpdatedAt = now
            };
            db.AccessSources.Add(source);
        }
        else
        {
            source.OwnerLogin = installation.OwnerLogin;
            source.OwnerType = installation.OwnerType;
            source.ActiveInstallationKey = GitHubAccessTypes.BuildActiveInstallationKey(
                projectId,
                installation.InstallationId);
            source.UpdatedAt = now;
        }

        var repositoryEntity = await db.Repositories.SingleOrDefaultAsync(
            item => item.SourceId == source.Id && item.GitHubRepositoryId == repository.Id,
            cancellationToken);
        if (repositoryEntity is null)
        {
            repositoryEntity = new GitHubRepository
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                GitHubRepositoryId = repository.Id,
                FullName = repository.FullName,
                Name = repository.Name,
                OwnerLogin = repository.OwnerLogin,
                DefaultBranch = repository.DefaultBranch,
                Url = repository.HtmlUrl,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Repositories.Add(repositoryEntity);
        }
        else
        {
            repositoryEntity.FullName = repository.FullName;
            repositoryEntity.Name = repository.Name;
            repositoryEntity.OwnerLogin = repository.OwnerLogin;
            repositoryEntity.DefaultBranch = repository.DefaultBranch;
            repositoryEntity.Url = repository.HtmlUrl;
            repositoryEntity.UpdatedAt = now;
        }

        var makePrimary = existingLinks.All(link => !link.Primary);
        var link = new ProjectRepositoryLink
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SourceId = source.Id,
            GitHubRepositoryId = repositoryEntity.Id,
            GitHubRepoId = repository.Id,
            LinkedByUserId = request.InitiatingUserId,
            AccessType = GitHubAccessTypes.InstallationRequested,
            FullName = repository.FullName,
            Name = repository.Name,
            CustomName = null,
            OwnerLogin = repository.OwnerLogin,
            DefaultBranch = repository.DefaultBranch,
            Url = repository.HtmlUrl,
            Active = true,
            Primary = makePrimary,
            Enabled = true,
            ActiveRepositoryKey = $"{projectId:N}:{repository.Id}",
            PrimaryProjectKey = makePrimary ? projectId.ToString("N") : null,
            LinkedAt = now,
            LastSyncedAt = null,
            SyncStatus = GitHubSyncStatuses.Pending,
            UpdatedAt = now
        };
        db.ProjectRepositoryLinks.Add(link);

        request.GitHubRepositoryId = repository.Id;
        request.Status = GitHubRepositoryAccessRequestStatuses.Completed;
        request.CompletedAt = now;
        request.ConsumedAt = now;
        request.FailureCode = null;
        request.Version++;

        var commitTime = Later(now, _timeProvider.GetUtcNow().UtcDateTime);
        if (request.ExpiresAt <= commitTime)
        {
            // Discard all staged connection changes before materializing expiry.
            db.ChangeTracker.Clear();
            await db.RepositoryAccessRequests
                .Where(item => item.Id == requestId
                    && item.Status == GitHubRepositoryAccessRequestStatuses.Pending
                    && item.ExpiresAt <= commitTime)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, GitHubRepositoryAccessRequestStatuses.Expired)
                    .SetProperty(item => item.ConsumedAt, commitTime)
                    .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Failed(request, GitHubRepositoryAccessFailureCodes.RequestExpired);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OwnerGrantedRepositoryCompletionPersistenceResult(
            request.Id,
            projectId,
            true,
            false,
            null,
            source.Id,
            repositoryEntity.Id,
            link.Id,
            repository.Id,
            new InitialRepositorySyncRequest(
                projectId,
                link.Id,
                source.Id,
                repositoryEntity.Id,
                repository.Id));
    }

    private static async Task<OwnerGrantedRepositoryCompletionPersistenceResult> FailAndCommitAsync(
        GitHubDbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        GitHubRepositoryAccessRequest request,
        string errorCode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        GitHubRepositoryAccessFailureCodes.RequireSafe(errorCode);
        request.Status = GitHubRepositoryAccessRequestStatuses.Failed;
        request.FailureCode = errorCode;
        request.ConsumedAt = now;
        request.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Failed(request, errorCode);
    }

    private static async Task<OwnerGrantedRepositoryCompletionPersistenceResult> ResolveCompletedAsync(
        GitHubDbContext db,
        GitHubRepositoryAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (request.GitHubRepositoryId is not long gitHubRepositoryId)
        {
            return Failed(request, GitHubRepositoryAccessFailureCodes.CompletionInconsistent);
        }

        var link = await db.ProjectRepositoryLinks
            .AsNoTracking()
            .Where(item => item.ProjectId == request.ProjectId
                && item.GitHubRepoId == gitHubRepositoryId)
            .OrderByDescending(item => item.Active)
            .ThenByDescending(item => item.LinkedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (link is null)
        {
            return Failed(request, GitHubRepositoryAccessFailureCodes.CompletionInconsistent);
        }

        return new OwnerGrantedRepositoryCompletionPersistenceResult(
            request.Id,
            request.ProjectId,
            true,
            true,
            null,
            link.SourceId,
            link.GitHubRepositoryId,
            link.Id,
            link.GitHubRepoId,
            null);
    }



    private static DateTime Later(DateTime first, DateTime second) => first > second ? first : second;

    private static ApiException CompletionInProgress(Exception? inner = null) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.Conflict,
        "Repository completion is already being updated. Please retry.",
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

    private static OwnerGrantedRepositoryCompletionPersistenceResult Missing(Guid requestId) => new(
        requestId,
        Guid.Empty,
        false,
        false,
        GitHubRepositoryAccessFailureCodes.RequestNotFound,
        null,
        null,
        null,
        null,
        null);

    private static OwnerGrantedRepositoryCompletionPersistenceResult Failed(
        GitHubRepositoryAccessRequest request,
        string errorCode)
    {
        GitHubRepositoryAccessFailureCodes.RequireSafe(errorCode);
        return new(
        request.Id,
        request.ProjectId,
        false,
        false,
        errorCode,
        null,
        null,
        null,
        request.GitHubRepositoryId,
        null);
    }
}
