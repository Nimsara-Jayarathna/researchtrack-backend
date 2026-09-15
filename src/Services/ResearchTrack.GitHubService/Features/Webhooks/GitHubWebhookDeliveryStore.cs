using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public sealed class GitHubWebhookDeliveryStore : IGitHubWebhookDeliveryStore
{
    private readonly IDbContextFactory<GitHubDbContext> _dbContextFactory;
    private readonly GitHubWebhookOptions _options;

    public GitHubWebhookDeliveryStore(
        IDbContextFactory<GitHubDbContext> dbContextFactory,
        GitHubWebhookOptions options)
    {
        _dbContextFactory = dbContextFactory;
        _options = options;
    }

    public async Task<GitHubWebhookAcceptResult> AcceptAsync(
        GitHubWebhookEnvelope envelope,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.WebhookDeliveries.SingleOrDefaultAsync(
            item => item.DeliveryId == envelope.DeliveryId,
            cancellationToken);
        if (existing is not null)
        {
            EnsureSamePayload(existing, envelope);
            return new GitHubWebhookAcceptResult(
                existing.Id,
                existing.DeliveryId,
                existing.EventType,
                true,
                existing.Status);
        }

        var delivery = new GitHubWebhookDelivery
        {
            Id = Guid.NewGuid(),
            DeliveryId = envelope.DeliveryId,
            EventType = envelope.EventType,
            Action = envelope.Action,
            InstallationId = envelope.InstallationId,
            GitHubRepositoryId = envelope.GitHubRepositoryId,
            PayloadJson = envelope.PayloadJson,
            PayloadSha256 = envelope.PayloadSha256,
            Status = GitHubWebhookDeliveryStatuses.Received,
            AttemptCount = 0,
            ReceivedAt = now,
            NextAttemptAt = now,
            UpdatedAt = now
        };
        db.WebhookDeliveries.Add(delivery);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            db.ChangeTracker.Clear();
            existing = await db.WebhookDeliveries.AsNoTracking().SingleAsync(
                item => item.DeliveryId == envelope.DeliveryId,
                cancellationToken);
            EnsureSamePayload(existing, envelope);
            return new GitHubWebhookAcceptResult(
                existing.Id,
                existing.DeliveryId,
                existing.EventType,
                true,
                existing.Status);
        }

        return new GitHubWebhookAcceptResult(
            delivery.Id,
            delivery.DeliveryId,
            delivery.EventType,
            false,
            delivery.Status);
    }

    public async Task<GitHubWebhookDelivery?> TryClaimNextDueAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        var leaseCutoff = now - _options.ProcessingLease;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var candidate = await db.WebhookDeliveries.AsNoTracking()
                .Where(item =>
                    (item.Status == GitHubWebhookDeliveryStatuses.Received
                        && item.AttemptCount < _options.MaxAttempts
                        && (item.NextAttemptAt == null || item.NextAttemptAt <= now))
                    || (item.Status == GitHubWebhookDeliveryStatuses.Failed
                        && item.AttemptCount < _options.MaxAttempts
                        && item.NextAttemptAt != null
                        && item.NextAttemptAt <= now)
                    || (item.Status == GitHubWebhookDeliveryStatuses.Processing
                        && item.ProcessingStartedAt != null
                        && item.ProcessingStartedAt <= leaseCutoff))
                .OrderBy(item => item.NextAttemptAt ?? item.ReceivedAt)
                .ThenBy(item => item.ReceivedAt)
                .Select(item => new { item.Id, item.Status, item.AttemptCount })
                .FirstOrDefaultAsync(cancellationToken);
            if (candidate is null)
            {
                return null;
            }

            if (candidate.Status == GitHubWebhookDeliveryStatuses.Processing
                && candidate.AttemptCount >= _options.MaxAttempts)
            {
                await db.WebhookDeliveries
                    .Where(item => item.Id == candidate.Id
                        && item.Status == GitHubWebhookDeliveryStatuses.Processing
                        && item.AttemptCount >= _options.MaxAttempts
                        && item.ProcessingStartedAt != null
                        && item.ProcessingStartedAt <= leaseCutoff)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, GitHubWebhookDeliveryStatuses.Failed)
                        .SetProperty(item => item.ProcessingStartedAt, (DateTime?)null)
                        .SetProperty(item => item.NextAttemptAt, (DateTime?)null)
                        .SetProperty(item => item.LastError,
                            "GitHub webhook processing lease expired after the maximum retry count.")
                        .SetProperty(item => item.UpdatedAt, now), cancellationToken);
                continue;
            }

            var affected = await db.WebhookDeliveries
                .Where(item => item.Id == candidate.Id
                    && item.AttemptCount < _options.MaxAttempts
                    && ((item.Status == GitHubWebhookDeliveryStatuses.Received
                            && (item.NextAttemptAt == null || item.NextAttemptAt <= now))
                        || (item.Status == GitHubWebhookDeliveryStatuses.Failed
                            && item.NextAttemptAt != null
                            && item.NextAttemptAt <= now)
                        || (item.Status == GitHubWebhookDeliveryStatuses.Processing
                            && item.ProcessingStartedAt != null
                            && item.ProcessingStartedAt <= leaseCutoff)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, GitHubWebhookDeliveryStatuses.Processing)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                    .SetProperty(item => item.ProcessingStartedAt, now)
                    .SetProperty(item => item.NextAttemptAt, (DateTime?)null)
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken);
            if (affected != 1)
            {
                continue;
            }

            return await db.WebhookDeliveries.AsNoTracking().SingleAsync(
                item => item.Id == candidate.Id,
                cancellationToken);
        }

        return null;
    }

    public async Task<DateTime?> GetNextDueAtAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var pending = await db.WebhookDeliveries.AsNoTracking()
            .Where(item => item.AttemptCount < _options.MaxAttempts
                && (item.Status == GitHubWebhookDeliveryStatuses.Received
                    || (item.Status == GitHubWebhookDeliveryStatuses.Failed && item.NextAttemptAt != null)))
            .Select(item => (DateTime?)(item.NextAttemptAt ?? item.ReceivedAt))
            .OrderBy(value => value)
            .FirstOrDefaultAsync(cancellationToken);

        var staleProcessingStart = await db.WebhookDeliveries.AsNoTracking()
            .Where(item => item.Status == GitHubWebhookDeliveryStatuses.Processing
                && item.ProcessingStartedAt != null)
            .Select(item => item.ProcessingStartedAt)
            .OrderBy(value => value)
            .FirstOrDefaultAsync(cancellationToken);

        DateTime? processingDue = staleProcessingStart.HasValue
            ? staleProcessingStart.Value + _options.ProcessingLease
            : null;
        if (processingDue.HasValue && processingDue.Value <= now)
        {
            processingDue = now;
        }

        if (!pending.HasValue) return processingDue;
        if (!processingDue.HasValue) return pending;
        return pending.Value <= processingDue.Value ? pending : processingDue;
    }

    public async Task RenewProcessingLeaseAsync(
        Guid id,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.WebhookDeliveries
            .Where(item => item.Id == id && item.Status == GitHubWebhookDeliveryStatuses.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ProcessingStartedAt, now)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
    }

    public async Task MarkProcessedAsync(
        Guid id,
        bool ignored,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.WebhookDeliveries
            .Where(item => item.Id == id && item.Status == GitHubWebhookDeliveryStatuses.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, ignored
                    ? GitHubWebhookDeliveryStatuses.Ignored
                    : GitHubWebhookDeliveryStatuses.Processed)
                .SetProperty(item => item.ProcessedAt, now)
                .SetProperty(item => item.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(item => item.NextAttemptAt, (DateTime?)null)
                .SetProperty(item => item.LastError, (string?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid id,
        string error,
        DateTime now,
        DateTime? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.WebhookDeliveries
            .Where(item => item.Id == id && item.Status == GitHubWebhookDeliveryStatuses.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, GitHubWebhookDeliveryStatuses.Failed)
                .SetProperty(item => item.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(item => item.NextAttemptAt, nextAttemptAt)
                .SetProperty(item => item.LastError, Truncate(error, 2048))
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
    }

    private static void EnsureSamePayload(GitHubWebhookDelivery existing, GitHubWebhookEnvelope envelope)
    {
        if (!string.Equals(existing.PayloadSha256, envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.EventType, envelope.EventType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The GitHub delivery identifier was already received with different content.");
        }
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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
