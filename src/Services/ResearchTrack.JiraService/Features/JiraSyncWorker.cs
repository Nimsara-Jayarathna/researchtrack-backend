using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Persistence;
namespace ResearchTrack.JiraService.Features;

public sealed class JiraSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes; private readonly JiraOptions _options; private readonly ILogger<JiraSyncWorker> _logger;
    public JiraSyncWorker(IServiceScopeFactory scopes,JiraOptions options,ILogger<JiraSyncWorker> logger){_scopes=scopes;_options=options;_logger=logger;}
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await QueueReconciliationAsync(stoppingToken);await RefreshWebhookRegistrationsAsync(stoppingToken);await ProcessOneAsync(stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}catch(Exception ex){_logger.LogError(ex,"Jira synchronization worker iteration failed.");}
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1,_options.SyncWorkerPollSeconds)),stoppingToken);
        }
    }
    private async Task QueueReconciliationAsync(CancellationToken ct)
    {
        using var scope=_scopes.CreateScope();var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();await using var db=await factory.CreateDbContextAsync(ct);var cutoff=DateTimeOffset.UtcNow.AddMinutes(-Math.Max(1,_options.ReconciliationIntervalMinutes));var ids=await db.JiraConnections.Where(x=>!x.LastReconciledAt.HasValue||x.LastReconciledAt<cutoff).Select(x=>x.ResearchProjectId).ToListAsync(ct);var now=DateTimeOffset.UtcNow;
        foreach(var id in ids){if(await db.JiraSyncJobs.AnyAsync(x=>x.ResearchProjectId==id&&(x.Status=="PENDING"||x.Status=="RUNNING"),ct))continue;db.JiraSyncJobs.Add(new(){Id=Guid.NewGuid(),ResearchProjectId=id,Reason="RECONCILIATION",Scope="FULL_PROJECT",Status="PENDING",RequestedAt=now,AvailableAt=now});}
        if(ids.Count>0)await db.SaveChangesAsync(ct);
    }
    private async Task RefreshWebhookRegistrationsAsync(CancellationToken ct)
    {
        using var scope=_scopes.CreateScope();var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();var webhooks=scope.ServiceProvider.GetRequiredService<IJiraWebhookService>();await using var db=await factory.CreateDbContextAsync(ct);var cutoff=DateTimeOffset.UtcNow.AddDays(7);var ids=await db.JiraConnections.Where(x=>x.WebhookStatus!="ACTIVE"||!x.WebhookExpiresAt.HasValue||x.WebhookExpiresAt<cutoff).Select(x=>x.ResearchProjectId).Take(10).ToListAsync(ct);foreach(var id in ids)await webhooks.EnsureRegisteredAsync(id,ct);
    }
    private async Task ProcessOneAsync(CancellationToken ct)
    {
        Guid jobId;Guid projectId;
        using(var scope=_scopes.CreateScope()){var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();await using var db=await factory.CreateDbContextAsync(ct);var now=DateTimeOffset.UtcNow;var job=await db.JiraSyncJobs.Where(x=>x.Status=="PENDING"&&x.AvailableAt<=now).OrderBy(x=>x.RequestedAt).FirstOrDefaultAsync(ct);if(job is null)return;job.Status="RUNNING";job.StartedAt=now;job.AttemptCount++;await db.SaveChangesAsync(ct);jobId=job.Id;projectId=job.ResearchProjectId;}
        Exception? error=null;try{using var scope=_scopes.CreateScope();var sync=scope.ServiceProvider.GetRequiredService<IJiraSyncService>();await sync.SynchronizeAsync(projectId,ct);}catch(Exception ex){error=ex;_logger.LogWarning(ex,"Queued Jira synchronization failed for project {ProjectId}.",projectId);}
        using(var scope=_scopes.CreateScope()){var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();await using var db=await factory.CreateDbContextAsync(ct);var job=await db.JiraSyncJobs.SingleOrDefaultAsync(x=>x.Id==jobId,ct);if(job is null)return;var now=DateTimeOffset.UtcNow;if(error is null){job.Status="COMPLETED";job.CompletedAt=now;job.LastError=null;var c=await db.JiraConnections.SingleOrDefaultAsync(x=>x.ResearchProjectId==projectId,ct);if(c is not null)c.LastReconciledAt=now;var events=await db.JiraWebhookEvents.Where(x=>x.ResearchProjectId==projectId&&x.Status=="RECEIVED").ToListAsync(ct);foreach(var e in events){e.Status="PROCESSED";e.ProcessedAt=now;e.AttemptCount++;}}else if(job.AttemptCount>=5){job.Status="FAILED";job.CompletedAt=now;job.LastError=error.Message;var events=await db.JiraWebhookEvents.Where(x=>x.ResearchProjectId==projectId&&x.Status=="RECEIVED").ToListAsync(ct);foreach(var e in events){e.Status="FAILED";e.ProcessedAt=now;e.AttemptCount++;e.LastError=error.Message;}}else{job.Status="PENDING";job.LastError=error.Message;job.AvailableAt=now.Add(RetryDelay(job.AttemptCount));}await db.SaveChangesAsync(ct);}
    }
    private static TimeSpan RetryDelay(int attempt)=>attempt switch{<=1=>TimeSpan.FromSeconds(30),2=>TimeSpan.FromMinutes(2),3=>TimeSpan.FromMinutes(5),_=>TimeSpan.FromMinutes(15)};
}
