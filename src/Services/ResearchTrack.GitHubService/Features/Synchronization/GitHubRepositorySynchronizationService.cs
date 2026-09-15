using Prometheus;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class GitHubRepositorySynchronizationService : IGitHubRepositorySynchronizationService
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly IGitHubRepositorySyncClient _gitHubClient;
    private readonly IGitHubInstallationTokenProvider _tokenProvider;
    private readonly IGitHubAppClient _gitHubAppClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubRepositorySynchronizationService> _logger;
    private static readonly Counter SyncRunsTotal = Metrics.CreateCounter("github_sync_runs_total","Total number of GitHub repository sync attempts.",new CounterConfiguration { LabelNames = new[] { "trigger", "outcome" } });
    private static readonly Histogram SyncDurationSeconds = Metrics.CreateHistogram("github_sync_duration_seconds","Duration of GitHub repository sync operations that actually ran.",new HistogramConfiguration { LabelNames = new[] { "trigger" } });

    public GitHubRepositorySynchronizationService(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        IGitHubRepositorySyncClient gitHubClient,
        IGitHubInstallationTokenProvider tokenProvider,
        IGitHubAppClient gitHubAppClient,
        TimeProvider timeProvider,
        ILogger<GitHubRepositorySynchronizationService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _gitHubClient = gitHubClient;
        _tokenProvider = tokenProvider;
        _gitHubAppClient = gitHubAppClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

        public async Task<GitHubSynchronizationOutcome> SynchronizeAsync(
        Guid linkedRepositoryId,
        string trigger,
        CancellationToken cancellationToken)
    {
        var syncContext = await LoadSyncContextAsync(linkedRepositoryId, cancellationToken);
        if (!syncContext.Link.Active
            || !syncContext.Link.Enabled
            || !string.Equals(syncContext.Source.ConnectionStatus, GitHubConnectionStatuses.Connected, StringComparison.Ordinal)
            || !syncContext.Repository.Available)
        {
            SyncRunsTotal.WithLabels(trigger, "skipped_unavailable").Inc();
            return GitHubSynchronizationOutcome.SkippedUnavailable;
        }

        var runId = Guid.NewGuid();
        if (!await TryMarkStartedAsync(syncContext.Link, runId, trigger, cancellationToken))
        {
            SyncRunsTotal.WithLabels(trigger, "already_running").Inc();
            return GitHubSynchronizationOutcome.AlreadyRunning;
        }

        using var durationTimer = SyncDurationSeconds.WithLabels(trigger).NewTimer();

        try
        {
            if (syncContext.Source.InstallationId is not long installationId)
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.Conflict,
                    "The linked repository does not have an active GitHub App installation.");
            }

            var installation = await _gitHubAppClient.GetInstallationAsync(
                installationId,
                cancellationToken);
            GitHubAppPermissionRequirements.EnsureInstallationActive(installation);
            GitHubAppPermissionRequirements.EnsureSynchronizationReadPermissions(installation);

            var token = await _tokenProvider.GetTokenAsync(installationId, cancellationToken);

            var repository = await _gitHubClient.GetRepositoryAsync(
                syncContext.Link.OwnerLogin,
                syncContext.Link.Name,
                token,
                cancellationToken);

            if (repository.Id != syncContext.Link.GitHubRepoId)
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.Conflict,
                    "The GitHub repository identity no longer matches the linked ResearchTrack repository.");
            }

            // ResearchTrack tracks default-branch activity only. Listing every
            // branch adds an unnecessary GitHub API call and used to make initial
            // synchronization fail before the default-branch commit request. Use
            // the newest default-branch commit as the local branch head instead.
            var commits = (await _gitHubClient.GetCommitsAsync(
                repository.OwnerLogin,
                repository.Name,
                repository.DefaultBranch,
                token,
                cancellationToken)).ToList();
            IReadOnlyList<GitHubSyncBranch> branches = commits.Count == 0
                ? []
                : [new GitHubSyncBranch(repository.DefaultBranch, commits[0].Sha, false)];

            var contributors = await _gitHubClient.GetContributorsAsync(
                repository.OwnerLogin,
                repository.Name,
                token,
                cancellationToken);

            var pullRequests = (await _gitHubClient.GetPullRequestsAsync(
                repository.OwnerLogin,
                repository.Name,
                token,
                cancellationToken)).ToList();

            await EnrichNewCommitsAsync(
                linkedRepositoryId,
                repository,
                commits,
                token,
                cancellationToken);

            var reviews = await EnrichPullRequestsAsync(
                linkedRepositoryId,
                repository,
                pullRequests,
                token,
                cancellationToken);

            await PersistSnapshotAsync(
                syncContext,
                runId,
                repository,
                branches,
                commits,
                contributors,
                pullRequests,
                reviews,
                cancellationToken);

            _logger.LogInformation(
                "GitHub synchronization completed. ProjectId={ProjectId} LinkedRepositoryId={LinkedRepositoryId} Trigger={Trigger} Commits={CommitCount} Contributors={ContributorCount} PullRequests={PullRequestCount}",
                syncContext.Link.ProjectId,
                linkedRepositoryId,
                trigger,
                commits.Count,
                contributors.Count,
                pullRequests.Count);
            SyncRunsTotal.WithLabels(trigger, "completed").Inc();
            return GitHubSynchronizationOutcome.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SyncRunsTotal.WithLabels(trigger, "cancelled").Inc();
            await MarkFailedBestEffortAsync(
                linkedRepositoryId,
                runId,
                "cancelled",
                "GitHub synchronization was cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            SyncRunsTotal.WithLabels(trigger, "failed").Inc();
            var errorCode = exception is ApiException apiException
                ? apiException.Code
                : "github_sync_failed";
            await MarkFailedBestEffortAsync(
                linkedRepositoryId,
                runId,
                errorCode,
                exception.Message);

            _logger.LogError(
                exception,
                "GitHub synchronization failed. LinkedRepositoryId={LinkedRepositoryId} Trigger={Trigger}",
                linkedRepositoryId,
                trigger);
            throw;
        }
    }



    private async Task<SyncContext> LoadSyncContextAsync(
        Guid linkedRepositoryId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var link = await dbContext.ProjectRepositoryLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(link => link.Id == linkedRepositoryId, cancellationToken)
            ?? throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The linked GitHub repository was not found.");

        var source = await dbContext.AccessSources
            .AsNoTracking()
            .SingleOrDefaultAsync(
                source => source.Id == link.SourceId && source.Active,
                cancellationToken)
            ?? throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The GitHub access source is no longer active.");

        var repository = await dbContext.Repositories
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == link.GitHubRepositoryId, cancellationToken)
            ?? throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The GitHub repository inventory record was not found.");

        return new SyncContext(link, source, repository);
    }

    private async Task<bool> TryMarkStartedAsync(
        ProjectRepositoryLink link,
        Guid runId,
        string trigger,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var affected = await dbContext.ProjectRepositoryLinks
            .Where(item => item.Id == link.Id
                && item.Active
                && item.Enabled
                && item.SyncStatus != GitHubSyncStatuses.InProgress)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.SyncStatus, GitHubSyncStatuses.InProgress)
                .SetProperty(item => item.LastSyncStartedAt, now)
                .SetProperty(item => item.LastSyncError, (string?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.SyncRuns.Add(new GitHubSyncRun
        {
            Id = runId,
            ProjectId = link.ProjectId,
            RepositoryLinkId = link.Id,
            Trigger = trigger,
            Status = GitHubSyncRunStatuses.Running,
            StartedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task EnrichNewCommitsAsync(
        Guid linkedRepositoryId,
        GitHubSyncRepository repository,
        List<GitHubSyncCommit> commits,
        string? token,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var detailedShas = (await dbContext.Commits
            .AsNoTracking()
            .Where(commit =>
                commit.RepositoryLinkId == linkedRepositoryId
                && commit.Additions != null)
            .Select(commit => commit.Sha)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < commits.Count; index++)
        {
            if (detailedShas.Contains(commits[index].Sha))
            {
                continue;
            }

            commits[index] = await _gitHubClient.GetCommitAsync(
                repository.OwnerLogin,
                repository.Name,
                commits[index].Sha,
                token,
                cancellationToken);
        }
    }

    private async Task<Dictionary<long, IReadOnlyList<GitHubSyncReview>>> EnrichPullRequestsAsync(
        Guid linkedRepositoryId,
        GitHubSyncRepository repository,
        List<GitHubSyncPullRequest> pullRequests,
        string? token,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var knownUpdates = await dbContext.PullRequests
            .AsNoTracking()
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .Select(item => new { item.GitHubPullRequestId, item.UpdatedAt })
            .ToListAsync(cancellationToken);
        var knownById = knownUpdates.ToDictionary(
            item => item.GitHubPullRequestId,
            item => item.UpdatedAt);

        var reviews = new Dictionary<long, IReadOnlyList<GitHubSyncReview>>();
        for (var index = 0; index < pullRequests.Count; index++)
        {
            var pullRequest = pullRequests[index];
            var hasKnownVersion = knownById.TryGetValue(
                pullRequest.Id,
                out var knownUpdatedAt);
            var needsDetail = string.Equals(
                    pullRequest.State,
                    "OPEN",
                    StringComparison.Ordinal)
                || !hasKnownVersion
                || knownUpdatedAt != pullRequest.UpdatedAt;

            if (!needsDetail)
            {
                continue;
            }

            pullRequest = await _gitHubClient.GetPullRequestAsync(
                repository.OwnerLogin,
                repository.Name,
                pullRequest.Number,
                token,
                cancellationToken);
            pullRequests[index] = pullRequest;

            reviews[pullRequest.Id] = await _gitHubClient.GetPullRequestReviewsAsync(
                repository.OwnerLogin,
                repository.Name,
                pullRequest.Number,
                token,
                cancellationToken);
        }

        return reviews;
    }

    private async Task PersistSnapshotAsync(
        SyncContext syncContext,
        Guid runId,
        GitHubSyncRepository repository,
        IReadOnlyList<GitHubSyncBranch> branches,
        IReadOnlyList<GitHubSyncCommit> commits,
        IReadOnlyList<GitHubSyncContributor> contributors,
        IReadOnlyList<GitHubSyncPullRequest> pullRequests,
        IReadOnlyDictionary<long, IReadOnlyList<GitHubSyncReview>> reviews,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var link = await dbContext.ProjectRepositoryLinks
            .SingleAsync(item => item.Id == syncContext.Link.Id, cancellationToken);
        var repositoryEntity = await dbContext.Repositories
            .SingleAsync(item => item.Id == link.GitHubRepositoryId, cancellationToken);

        UpdateRepositoryMetadata(link, repositoryEntity, repository, branches, commits, now);
        await UpsertBranchesAsync(dbContext, link.Id, branches, now, cancellationToken);
        await UpsertCommitsAsync(dbContext, link.Id, commits, now, cancellationToken);
        await UpsertContributorsAsync(dbContext, link.Id, contributors, commits, now, cancellationToken);
        var pullRequestEntities = await UpsertPullRequestsAsync(
            dbContext,
            link.Id,
            pullRequests,
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await UpsertReviewsAsync(
            dbContext,
            pullRequestEntities,
            reviews,
            now,
            cancellationToken);

        var syncRun = await dbContext.SyncRuns
            .SingleAsync(item => item.Id == runId, cancellationToken);
        syncRun.Status = GitHubSyncRunStatuses.Success;
        syncRun.CompletedAt = now;
        syncRun.CommitsFetched = commits.Count;
        syncRun.ContributorsFetched = contributors.Count;
        syncRun.PullRequestsFetched = pullRequests.Count;
        syncRun.ReviewsFetched = reviews.Values.Sum(items => items.Count);
        syncRun.BranchesFetched = branches.Count;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void UpdateRepositoryMetadata(
        ProjectRepositoryLink link,
        GitHubRepository repositoryEntity,
        GitHubSyncRepository repository,
        IReadOnlyList<GitHubSyncBranch> branches,
        IReadOnlyList<GitHubSyncCommit> commits,
        DateTime now)
    {
        link.FullName = repository.FullName;
        link.Name = repository.Name;
        link.OwnerLogin = repository.OwnerLogin;
        link.DefaultBranch = repository.DefaultBranch;
        link.Url = repository.HtmlUrl;
        link.LastKnownHeadSha = branches
            .FirstOrDefault(branch => string.Equals(
                branch.Name,
                repository.DefaultBranch,
                StringComparison.Ordinal))
            ?.HeadSha
            ?? commits.FirstOrDefault()?.Sha;
        link.LastSyncedAt = now;
        link.SyncStatus = GitHubSyncStatuses.Success;
        link.LastSyncError = null;
        link.UpdatedAt = now;

        repositoryEntity.FullName = repository.FullName;
        repositoryEntity.Name = repository.Name;
        repositoryEntity.OwnerLogin = repository.OwnerLogin;
        repositoryEntity.DefaultBranch = repository.DefaultBranch;
        repositoryEntity.Url = repository.HtmlUrl;
        repositoryEntity.UpdatedAt = now;
    }

    private static async Task UpsertBranchesAsync(
        GitHubDbContext dbContext,
        Guid linkedRepositoryId,
        IReadOnlyList<GitHubSyncBranch> branches,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Branches
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .ToListAsync(cancellationToken);
        var byName = existing.ToDictionary(item => item.Name, StringComparer.Ordinal);

        foreach (var branch in branches)
        {
            if (!byName.TryGetValue(branch.Name, out var entity))
            {
                entity = new GitHubBranch
                {
                    Id = Guid.NewGuid(),
                    RepositoryLinkId = linkedRepositoryId,
                    Name = branch.Name,
                    HeadSha = branch.HeadSha,
                    IsProtected = branch.Protected,
                    LastSeenAt = now
                };
                dbContext.Branches.Add(entity);
                byName[branch.Name] = entity;
            }
            else
            {
                entity.HeadSha = branch.HeadSha;
                entity.IsProtected = branch.Protected;
                entity.LastSeenAt = now;
            }
        }
    }

    private static async Task UpsertCommitsAsync(
        GitHubDbContext dbContext,
        Guid linkedRepositoryId,
        IReadOnlyList<GitHubSyncCommit> commits,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Commits
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .ToListAsync(cancellationToken);
        var bySha = existing.ToDictionary(item => item.Sha, StringComparer.Ordinal);

        foreach (var commit in commits)
        {
            if (!bySha.TryGetValue(commit.Sha, out var entity))
            {
                entity = new GitHubCommit
                {
                    Id = Guid.NewGuid(),
                    RepositoryLinkId = linkedRepositoryId,
                    Sha = commit.Sha,
                    Message = commit.Message,
                    HtmlUrl = commit.HtmlUrl,
                    FirstSeenAt = now,
                    LastSeenAt = now
                };
                dbContext.Commits.Add(entity);
                bySha[commit.Sha] = entity;
            }

            entity.Message = commit.Message;
            entity.AuthorGitHubId = commit.AuthorGitHubId;
            entity.AuthorLogin = commit.AuthorLogin;
            entity.AuthorName = commit.AuthorName;
            entity.AuthorEmail = commit.AuthorEmail;
            entity.CommitterGitHubId = commit.CommitterGitHubId;
            entity.CommitterLogin = commit.CommitterLogin;
            entity.AuthoredAt = commit.AuthoredAt;
            entity.CommittedAt = commit.CommittedAt;
            entity.HtmlUrl = commit.HtmlUrl;
            entity.ParentsCount = commit.ParentsCount;
            entity.Additions = commit.Additions ?? entity.Additions;
            entity.Deletions = commit.Deletions ?? entity.Deletions;
            entity.ChangedFiles = commit.ChangedFiles ?? entity.ChangedFiles;
            entity.LastSeenAt = now;
        }
    }

    private static async Task UpsertContributorsAsync(
        GitHubDbContext dbContext,
        Guid linkedRepositoryId,
        IReadOnlyList<GitHubSyncContributor> contributors,
        IReadOnlyList<GitHubSyncCommit> commits,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Contributors
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .ToListAsync(cancellationToken);
        var byUserId = existing.ToDictionary(item => item.GitHubUserId);

        var metricsByUserId = commits
            .Where(commit => commit.AuthorGitHubId.HasValue)
            .GroupBy(commit => commit.AuthorGitHubId!.Value)
            .ToDictionary(
                group => group.Key,
                group => new ContributorMetrics(
                    group.Count(),
                    group.Sum(commit => (long)(commit.Additions ?? 0)),
                    group.Sum(commit => (long)(commit.Deletions ?? 0)),
                    group.Sum(commit => (long)(commit.ChangedFiles ?? 0)),
                    MinDate(group.Select(commit => commit.CommittedAt)),
                    MaxDate(group.Select(commit => commit.CommittedAt))));

        foreach (var contributor in contributors)
        {
            if (!byUserId.TryGetValue(contributor.GitHubUserId, out var entity))
            {
                entity = new GitHubContributor
                {
                    Id = Guid.NewGuid(),
                    RepositoryLinkId = linkedRepositoryId,
                    GitHubUserId = contributor.GitHubUserId,
                    Login = contributor.Login
                };
                dbContext.Contributors.Add(entity);
                byUserId[contributor.GitHubUserId] = entity;
            }

            metricsByUserId.TryGetValue(contributor.GitHubUserId, out var metrics);
            entity.Login = contributor.Login;
            entity.AvatarUrl = contributor.AvatarUrl;
            entity.ProfileUrl = contributor.ProfileUrl;
            entity.GitHubContributionCount = contributor.Contributions;
            entity.ObservedCommitCount = metrics?.CommitCount ?? 0;
            entity.ObservedAdditions = metrics?.Additions ?? 0;
            entity.ObservedDeletions = metrics?.Deletions ?? 0;
            entity.ObservedChangedFiles = metrics?.ChangedFiles ?? 0;
            entity.FirstCommitAt = metrics?.FirstCommitAt;
            entity.LastCommitAt = metrics?.LastCommitAt;
            entity.LastSyncedAt = now;
        }
    }

    private static async Task<Dictionary<long, GitHubPullRequest>> UpsertPullRequestsAsync(
        GitHubDbContext dbContext,
        Guid linkedRepositoryId,
        IReadOnlyList<GitHubSyncPullRequest> pullRequests,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.PullRequests
            .Where(item => item.RepositoryLinkId == linkedRepositoryId)
            .ToListAsync(cancellationToken);
        var byGitHubId = existing.ToDictionary(item => item.GitHubPullRequestId);

        foreach (var pullRequest in pullRequests)
        {
            if (!byGitHubId.TryGetValue(pullRequest.Id, out var entity))
            {
                entity = new GitHubPullRequest
                {
                    Id = Guid.NewGuid(),
                    RepositoryLinkId = linkedRepositoryId,
                    GitHubPullRequestId = pullRequest.Id,
                    Number = pullRequest.Number,
                    Title = pullRequest.Title,
                    State = pullRequest.State,
                    SourceBranch = pullRequest.SourceBranch,
                    SourceSha = pullRequest.SourceSha,
                    TargetBranch = pullRequest.TargetBranch,
                    TargetSha = pullRequest.TargetSha,
                    CreatedAt = pullRequest.CreatedAt,
                    UpdatedAt = pullRequest.UpdatedAt,
                    HtmlUrl = pullRequest.HtmlUrl
                };
                dbContext.PullRequests.Add(entity);
                byGitHubId[pullRequest.Id] = entity;
            }

            entity.Number = pullRequest.Number;
            entity.Title = pullRequest.Title;
            entity.Body = pullRequest.Body;
            entity.State = pullRequest.State;
            entity.IsDraft = pullRequest.Draft;
            entity.IsMerged = pullRequest.Merged;
            entity.AuthorGitHubId = pullRequest.AuthorGitHubId;
            entity.AuthorLogin = pullRequest.AuthorLogin;
            entity.SourceBranch = pullRequest.SourceBranch;
            entity.SourceSha = pullRequest.SourceSha;
            entity.TargetBranch = pullRequest.TargetBranch;
            entity.TargetSha = pullRequest.TargetSha;
            entity.CreatedAt = pullRequest.CreatedAt;
            entity.UpdatedAt = pullRequest.UpdatedAt;
            entity.ClosedAt = pullRequest.ClosedAt;
            entity.MergedAt = pullRequest.MergedAt;
            entity.MergeCommitSha = pullRequest.MergeCommitSha;
            entity.HtmlUrl = pullRequest.HtmlUrl;
            entity.Additions = pullRequest.Additions ?? entity.Additions;
            entity.Deletions = pullRequest.Deletions ?? entity.Deletions;
            entity.ChangedFiles = pullRequest.ChangedFiles ?? entity.ChangedFiles;
            entity.CommitsCount = pullRequest.Commits ?? entity.CommitsCount;
            entity.CommentsCount = pullRequest.Comments ?? entity.CommentsCount;
            entity.ReviewCommentsCount = pullRequest.ReviewComments ?? entity.ReviewCommentsCount;
            entity.LastSyncedAt = now;
        }

        return byGitHubId;
    }

    private static async Task UpsertReviewsAsync(
        GitHubDbContext dbContext,
        IReadOnlyDictionary<long, GitHubPullRequest> pullRequestEntities,
        IReadOnlyDictionary<long, IReadOnlyList<GitHubSyncReview>> reviews,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var pair in reviews)
        {
            if (!pullRequestEntities.TryGetValue(pair.Key, out var pullRequest))
            {
                continue;
            }

            var existing = await dbContext.PullRequestReviews
                .Where(item => item.PullRequestId == pullRequest.Id)
                .ToListAsync(cancellationToken);
            var byReviewId = existing.ToDictionary(item => item.GitHubReviewId);

            foreach (var review in pair.Value)
            {
                if (!byReviewId.TryGetValue(review.Id, out var entity))
                {
                    entity = new GitHubPullRequestReview
                    {
                        Id = Guid.NewGuid(),
                        PullRequestId = pullRequest.Id,
                        GitHubReviewId = review.Id,
                        State = review.State,
                        LastSyncedAt = now
                    };
                    dbContext.PullRequestReviews.Add(entity);
                    byReviewId[review.Id] = entity;
                }

                entity.ReviewerGitHubId = review.ReviewerGitHubId;
                entity.ReviewerLogin = review.ReviewerLogin;
                entity.State = review.State;
                entity.SubmittedAt = review.SubmittedAt;
                entity.LastSyncedAt = now;
            }
        }
    }

    private async Task MarkFailedBestEffortAsync(
        Guid linkedRepositoryId,
        Guid runId,
        string errorCode,
        string errorMessage)
    {
        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var link = await dbContext.ProjectRepositoryLinks
                .SingleOrDefaultAsync(item => item.Id == linkedRepositoryId);
            if (link is not null)
            {
                link.SyncStatus = GitHubSyncStatuses.Failed;
                link.LastFailedSyncAt = now;
                link.LastSyncError = Truncate(errorMessage, 2048);
                link.UpdatedAt = now;
            }

            var syncRun = await dbContext.SyncRuns
                .SingleOrDefaultAsync(item => item.Id == runId);
            if (syncRun is not null)
            {
                syncRun.Status = GitHubSyncRunStatuses.Failed;
                syncRun.CompletedAt = now;
                syncRun.ErrorCode = Truncate(errorCode, 128);
                syncRun.ErrorMessage = Truncate(errorMessage, 2048);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to persist GitHub synchronization failure state. LinkedRepositoryId={LinkedRepositoryId}",
                linkedRepositoryId);
        }
    }

    private static DateTime? MinDate(IEnumerable<DateTime?> values)
    {
        var nonNull = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return nonNull.Count == 0 ? null : nonNull.Min();
    }

    private static DateTime? MaxDate(IEnumerable<DateTime?> values)
    {
        var nonNull = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return nonNull.Count == 0 ? null : nonNull.Max();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record SyncContext(ProjectRepositoryLink Link, GitHubAccessSource Source, GitHubRepository Repository);

    private sealed record ContributorMetrics(
        int CommitCount,
        long Additions,
        long Deletions,
        long ChangedFiles,
        DateTime? FirstCommitAt,
        DateTime? LastCommitAt);
}
