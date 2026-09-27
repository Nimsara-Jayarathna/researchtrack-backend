using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public interface IJiraSyncScheduler
{
    Task RequestAsync(Guid projectId, string reason, DateTimeOffset availableAt, string? entityId, CancellationToken ct);
}

/// <summary>
/// Durable, project-coalesced sync request scheduler. The MySQL named lock makes the
/// check/update/insert sequence safe even when multiple JiraService replicas receive work.
/// At most one PENDING follow-up is retained while another job is RUNNING.
/// </summary>
public sealed class JiraSyncScheduler : IJiraSyncScheduler
{
    private readonly IDbContextFactory<JiraDbContext> _factory;

    public JiraSyncScheduler(IDbContextFactory<JiraDbContext> factory) => _factory = factory;

    public async Task RequestAsync(Guid projectId, string reason, DateTimeOffset availableAt, string? entityId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var lease = await JiraDatabaseLock.TryAcquireAsync(db, JiraDatabaseLock.ProjectSchedule(projectId), 10, ct)
            ?? throw new InvalidOperationException($"Could not acquire Jira sync scheduling lock for project {projectId}.");

        var trigger = JiraOperationalMetrics.Trigger(reason);
        var connection = await db.JiraConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ResearchProjectId == projectId, ct);
        if (connection is null)
        {
            JiraOperationalMetrics.SyncRequests.WithLabels(trigger, "ignored_disconnected").Inc();
            return;
        }
        if (string.Equals(connection.SyncStatus, "INVALID_AUTH", StringComparison.Ordinal))
        {
            JiraOperationalMetrics.SyncRequests.WithLabels(trigger, "ignored_invalid_auth").Inc();
            return;
        }

        var pendingJobs = await db.JiraSyncJobs
            .Where(x => x.ResearchProjectId == projectId && x.Status == "PENDING")
            .OrderBy(x => x.RequestedAt)
            .ToListAsync(ct);
        var pending = pendingJobs.FirstOrDefault();

        var now = DateTimeOffset.UtcNow;
        if (pending is not null)
        {
            // Repair/coalesce any duplicate pending rows left by an older implementation.
            if (pendingJobs.Count > 1)
                db.JiraSyncJobs.RemoveRange(pendingJobs.Skip(1));
            // Coalesce into the existing request. Never delay work that was already due sooner.
            pending.RequestedAt = now;
            if (availableAt < pending.AvailableAt) pending.AvailableAt = availableAt;
            pending.Reason = MergeReason(pending.Reason, reason);
            pending.EntityId = pending.EntityId == entityId ? entityId : null;
            pending.LastError = null;
            await db.SaveChangesAsync(ct);
            JiraOperationalMetrics.SyncRequests.WithLabels(trigger, "coalesced").Inc();
            return;
        }

        db.JiraSyncJobs.Add(new JiraSyncJob
        {
            Id = Guid.NewGuid(),
            ResearchProjectId = projectId,
            Reason = reason,
            Scope = "FULL_PROJECT",
            EntityId = entityId,
            Status = "PENDING",
            RequestedAt = now,
            AvailableAt = availableAt
        });
        await db.SaveChangesAsync(ct);
        JiraOperationalMetrics.SyncRequests.WithLabels(trigger, "queued").Inc();
    }

    private static string MergeReason(string current, string incoming)
        => string.Equals(current, incoming, StringComparison.Ordinal) ? current : "COALESCED";
}
