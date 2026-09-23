using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Persistence;
namespace ResearchTrack.JiraService.Features;
public sealed class JiraSyncStateQueryService : IJiraSyncStateQueryService
{
    private readonly JiraDbContext _db; private readonly IProjectAuthorizationClient _auth;
    public JiraSyncStateQueryService(JiraDbContext db, IProjectAuthorizationClient auth){_db=db;_auth=auth;}
    public async Task<JiraProjectSyncStateResponse> GetAsync(Guid projectId,CancellationToken ct)
    {
        await _auth.EnsureCanAccessAsync(projectId,ct);
        var x=await _db.JiraConnections.AsNoTracking().SingleOrDefaultAsync(c=>c.ResearchProjectId==projectId,ct);
        return x is null
            ? new JiraProjectSyncStateResponse(false,0,null,null,null,null,null,null)
            : new JiraProjectSyncStateResponse(true,x.SyncRevision,x.SyncStatus,x.LastSyncedAt,x.LastSyncError,x.WebhookStatus,x.LastWebhookAt,x.LastReconciledAt);
    }
}
