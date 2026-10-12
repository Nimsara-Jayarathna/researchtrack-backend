
using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public interface IJiraSyncScheduler
{
    Task RequestAsync(
        Guid projectId,
        string reason,
        DateTimeOffset availableAt,
        string? entityId,
        CancellationToken ct
    );
}

/// <summary>
/// Durable, project-coalesced sync request scheduler.
/// The MySQL named lock makes the check/update/insert sequence safe
/// even when multiple JiraService replicas receive work.
/// At most one PENDING follow-up is retained while another job is RUNNING.
/// </summary>
public sealed class JiraSyncScheduler : IJiraSyncScheduler
{
    private readonly IDbContextFactory<JiraDbContext> _factory;

    public JiraSyncScheduler(IDbContextFactory<JiraDbContext> factory)
    {
        _factory = factory;
    }

    public async Task RequestAsync(
        Guid projectId,
        string reason,
        DateTimeOffset availableAt,
        string? entityId,
        CancellationToken ct
    )
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        await using var lease =
            await JiraDatabaseLock.TryAcquireAsync(
                db,
                JiraDatabaseLock.ProjectSchedule(projectId),
                10,
                ct
            )
            ?? throw new InvalidOperationException(
                $"Could not acquire Jira sync scheduling lock for project {projectId}."
            );

        await StageAsync(
            db,
            projectId,
            reason,
            availableAt,
            entityId,
            ct
        );

        // Persist the scheduled job while holding the project lock.
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Stages a Jira synchronization request in the supplied DbContext.
    /// Does not independently commit changes.
    ///
    /// Kafka consumers can stage their consumer receipt and sync job
    /// using the same DbContext and persist both atomically.
    ///
    /// The caller must hold the project scheduling lock and is
    /// responsible for committing the changes.
    /// </summary>
    internal static async Task StageAsync(
        JiraDbContext db,
        Guid projectId,
        string reason,
        DateTimeOffset availableAt,
        string? entityId,
        CancellationToken ct
    )
    {
        var trigger = JiraOperationalMetrics.Trigger(reason);

        var connection = await db
            .JiraConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ResearchProjectId == projectId,
                ct
            );

        if (connection is null)
        {
            JiraOperationalMetrics.SyncRequests
                .WithLabels(trigger, "ignored_disconnected")
                .Inc();

            return;
        }

        if (string.Equals(
            connection.SyncStatus,
            "INVALID_AUTH",
            StringComparison.Ordinal
        ))
        {
            JiraOperationalMetrics.SyncRequests
                .WithLabels(trigger, "ignored_invalid_auth")
                .Inc();

            return;
        }

        var pendingJobs = await db
            .JiraSyncJobs
            .Where(x =>
                x.ResearchProjectId == projectId
                && x.Status == "PENDING"
            )
            .OrderBy(x => x.RequestedAt)
            .ToListAsync(ct);

        var pending = pendingJobs.FirstOrDefault();
        var now = DateTimeOffset.UtcNow;

        if (pending is not null)
        {
            // Repair duplicate pending jobs from older implementations.
            if (pendingJobs.Count > 1)
            {
                db.JiraSyncJobs.RemoveRange(
                    pendingJobs.Skip(1)
                );
            }

            // Coalesce the request without delaying earlier work.
            pending.RequestedAt = now;

            if (availableAt < pending.AvailableAt)
            {
                pending.AvailableAt = availableAt;
            }

            pending.Reason = MergeReason(
                pending.Reason,
                reason
            );

            pending.EntityId =
                pending.EntityId == entityId
                    ? entityId
                    : null;

            pending.LastError = null;

            JiraOperationalMetrics.SyncRequests
                .WithLabels(trigger, "coalesced")
                .Inc();

            return;
        }

        // Stage the job without committing the DbContext.
        // The caller owns SaveChangesAsync and transaction boundaries.
        db.JiraSyncJobs.Add(
            new JiraSyncJob
            {
                Id = Guid.NewGuid(),
                ResearchProjectId = projectId,
                Reason = reason,
                Scope = "FULL_PROJECT",
                EntityId = entityId,
                Status = "PENDING",
                RequestedAt = now,
                AvailableAt = availableAt
            }
        );

        JiraOperationalMetrics.SyncRequests
            .WithLabels(trigger, "queued")
            .Inc();
    }

    private static string MergeReason(
        string current,
        string incoming
    ) =>
        string.Equals(
            current,
            incoming,
            StringComparison.Ordinal
        )
            ? current
            : "COALESCED";
}
