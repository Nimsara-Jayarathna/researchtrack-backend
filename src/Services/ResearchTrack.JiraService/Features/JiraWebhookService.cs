using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public interface IJiraWebhookService
{
    Task EnsureRegisteredAsync(Guid projectId,CancellationToken ct);
    Task RemoveAsync(Guid projectId,CancellationToken ct);
    Task<bool> ReceiveAsync(string? authorization,string? deliveryId,string payload,CancellationToken ct);
}

public sealed class JiraWebhookService : IJiraWebhookService
{
    private readonly IDbContextFactory<JiraDbContext> _factory; private readonly AtlassianClient _client; private readonly IJiraTokenProtector _protector; private readonly JiraOptions _options; private readonly IJiraSyncScheduler _scheduler; private readonly ILogger<JiraWebhookService> _logger;
    public JiraWebhookService(IDbContextFactory<JiraDbContext> factory,AtlassianClient client,IJiraTokenProtector protector,JiraOptions options,IJiraSyncScheduler scheduler,ILogger<JiraWebhookService> logger){_factory=factory;_client=client;_protector=protector;_options=options;_scheduler=scheduler;_logger=logger;}

    public async Task EnsureRegisteredAsync(Guid projectId,CancellationToken ct)
    {
        await using var db=await _factory.CreateDbContextAsync(ct); await using var webhookLease=await JiraDatabaseLock.TryAcquireAsync(db,JiraDatabaseLock.ProjectWebhook(projectId),30,ct); if(webhookLease is null)return; var c=await db.JiraConnections.SingleOrDefaultAsync(x=>x.ResearchProjectId==projectId,ct); if(c is null)return; if(string.Equals(c.SyncStatus,"INVALID_AUTH",StringComparison.Ordinal)){c.WebhookStatus="REAUTH_REQUIRED";await db.SaveChangesAsync(ct);return;}
        if(string.IsNullOrWhiteSpace(_options.WebhookUrl)||_options.WebhookUrl.Equals("CHANGE_ME",StringComparison.OrdinalIgnoreCase)){c.WebhookStatus="NOT_CONFIGURED";await db.SaveChangesAsync(ct);JiraOperationalMetrics.WebhookRegistration.WithLabels("ensure", "not_configured").Inc();return;}
        try
        {
            var token=await ValidTokenAsync(db,c,ct);
            if(c.WebhookId.HasValue && c.WebhookExpiresAt>DateTimeOffset.UtcNow.AddDays(7)){c.WebhookStatus="ACTIVE";await db.SaveChangesAsync(ct);JiraOperationalMetrics.WebhookRegistration.WithLabels("ensure", "already_active").Inc();return;}
            if(c.WebhookId.HasValue)
            {
                try { c.WebhookExpiresAt=await _client.RefreshWebhooksAsync(token,c.CloudId,new[]{c.WebhookId.Value},ct); c.WebhookStatus="ACTIVE";await db.SaveChangesAsync(ct);JiraOperationalMetrics.WebhookRegistration.WithLabels("refresh", "success").Inc();return; }
                catch(Exception ex){JiraOperationalMetrics.WebhookRegistration.WithLabels("refresh", "failed").Inc();_logger.LogWarning(ex,"Refreshing Jira webhook {WebhookId} failed; registering a replacement for project {ProjectId}.",c.WebhookId,projectId);}
            }
            var created=await _client.RegisterWebhookAsync(token,c.CloudId,c.JiraProjectKey,_options.WebhookUrl,ct); c.WebhookId=created.Id;c.WebhookExpiresAt=created.ExpiresAt;c.WebhookStatus="ACTIVE";c.UpdatedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);JiraOperationalMetrics.WebhookRegistration.WithLabels("register", "success").Inc();
        }
        catch(Exception ex){JiraOperationalMetrics.WebhookRegistration.WithLabels("ensure", "failed").Inc();if(ex is JiraTokenProtectionException){c.SyncStatus="INVALID_AUTH";c.LastSyncError=ex.Message;c.WebhookStatus="REAUTH_REQUIRED";}else{c.WebhookStatus="DEGRADED";}c.UpdatedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);_logger.LogWarning(ex,"Jira webhook registration failed for project {ProjectId}; scheduled reconciliation remains available.",projectId);}
    }
    public async Task RemoveAsync(Guid projectId,CancellationToken ct)
    {
        await using var db=await _factory.CreateDbContextAsync(ct);await using var webhookLease=await JiraDatabaseLock.TryAcquireAsync(db,JiraDatabaseLock.ProjectWebhook(projectId),30,ct);if(webhookLease is null)return;var c=await db.JiraConnections.SingleOrDefaultAsync(x=>x.ResearchProjectId==projectId,ct);if(c is null||!c.WebhookId.HasValue)return;
        try{var token=await ValidTokenAsync(db,c,ct);await _client.DeleteWebhooksAsync(token,c.CloudId,new[]{c.WebhookId.Value},ct);JiraOperationalMetrics.WebhookRegistration.WithLabels("delete", "success").Inc();}catch(Exception ex){JiraOperationalMetrics.WebhookRegistration.WithLabels("delete", "failed").Inc();_logger.LogWarning(ex,"Best-effort Jira webhook cleanup failed for project {ProjectId}.",projectId);}
    }
    public async Task<bool> ReceiveAsync(string? authorization,string? deliveryId,string payload,CancellationToken ct)
    {
        var receiveTimer = Stopwatch.StartNew();
        try
        {
        if(!ValidateBearer(authorization)){JiraOperationalMetrics.WebhookEvents.WithLabels("unknown", "rejected_auth").Inc();return false;}
        using var doc=JsonDocument.Parse(payload);var root=doc.RootElement;var eventType=Get(root,"webhookEvent")??Get(root,"issue_event_type_name")??"unknown";
        var metricEvent=JiraOperationalMetrics.Event(eventType);
        var issue=root.TryGetProperty("issue",out var i)?i:default;var jiraIssueId=issue.ValueKind==JsonValueKind.Object?Get(issue,"id"):null;var issueKey=issue.ValueKind==JsonValueKind.Object?Get(issue,"key"):null;
        string? projectKey=null;if(issue.ValueKind==JsonValueKind.Object&&issue.TryGetProperty("fields",out var f)&&f.TryGetProperty("project",out var p))projectKey=Get(p,"key");
        var matchedIds=new List<long>();if(root.TryGetProperty("matchedWebhookIds",out var mids)&&mids.ValueKind==JsonValueKind.Array)foreach(var x in mids.EnumerateArray())if(x.TryGetInt64(out var n))matchedIds.Add(n);
        await using var db=await _factory.CreateDbContextAsync(ct);JiraConnection? connection=null;
        if(matchedIds.Count>0)foreach(var webhookId in matchedIds){connection=await db.JiraConnections.FirstOrDefaultAsync(x=>x.WebhookId==webhookId,ct);if(connection is not null)break;}
        if(connection is null&&!string.IsNullOrWhiteSpace(projectKey))connection=await db.JiraConnections.FirstOrDefaultAsync(x=>x.JiraProjectKey==projectKey,ct);
        var cloudId=connection?.CloudId??"unknown";var id=string.IsNullOrWhiteSpace(deliveryId)?"body-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))):deliveryId;
        if(await db.JiraWebhookEvents.AnyAsync(x=>x.DeliveryId==id,ct)){JiraOperationalMetrics.WebhookEvents.WithLabels(metricEvent, "duplicate").Inc();return true;}
        var now=DateTimeOffset.UtcNow;db.JiraWebhookEvents.Add(new JiraWebhookEvent{Id=Guid.NewGuid(),DeliveryId=id,CloudId=cloudId,ResearchProjectId=connection?.ResearchProjectId,EventType=eventType,JiraIssueId=jiraIssueId,IssueKey=issueKey,PayloadJson=payload,ReceivedAt=now,Status=connection is null?"IGNORED":"RECEIVED"});
        Guid? projectId=null;
        if(connection is not null)
        {
            projectId=connection.ResearchProjectId;
            connection.LastWebhookAt=now;connection.WebhookStatus="ACTIVE";
        }
        await db.SaveChangesAsync(ct);
        JiraOperationalMetrics.WebhookEvents.WithLabels(metricEvent, projectId.HasValue ? "accepted" : "ignored_unmatched").Inc();
        if(projectId.HasValue)
            await _scheduler.RequestAsync(projectId.Value,"WEBHOOK",now.AddSeconds(Math.Max(1,_options.WebhookCoalesceSeconds)),jiraIssueId,ct);
        return true;
        }
        finally
        {
            JiraOperationalMetrics.WebhookReceiveDuration.Observe(receiveTimer.Elapsed.TotalSeconds);
        }
    }
    private bool ValidateBearer(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var jwt = authorizationHeader[7..].Trim();
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            using var jwtHeader = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[0]));
            if (!string.Equals(Get(jwtHeader.RootElement, "alg"), "HS256", StringComparison.Ordinal))
            {
                return false;
            }

            var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ClientSecret));
            var expectedSignature = hmac.ComputeHash(signingInput);
            var suppliedSignature = WebEncoders.Base64UrlDecode(parts[2]);

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, suppliedSignature))
            {
                return false;
            }

            using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
            if (payload.RootElement.TryGetProperty("exp", out var exp) &&
                exp.TryGetInt64(out var unix) &&
                DateTimeOffset.FromUnixTimeSeconds(unix) < DateTimeOffset.UtcNow.AddMinutes(-1))
            {
                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
    private async Task<string> ValidTokenAsync(JiraDbContext db,JiraConnection c,CancellationToken ct)
    {
        var accessToken=_protector.Unprotect(c.AccessTokenProtected);var changed=false;
        if(_protector.RequiresReprotection(c.AccessTokenProtected)){c.AccessTokenProtected=_protector.Protect(accessToken);changed=true;}
        string? refreshToken=null;var protectedRefreshToken=c.RefreshTokenProtected;
        if(!string.IsNullOrWhiteSpace(protectedRefreshToken)){refreshToken=_protector.Unprotect(protectedRefreshToken);if(_protector.RequiresReprotection(protectedRefreshToken)){c.RefreshTokenProtected=_protector.Protect(refreshToken);changed=true;}}
        if(!c.TokenExpiresAt.HasValue||c.TokenExpiresAt>DateTimeOffset.UtcNow.AddMinutes(2)){if(changed)await db.SaveChangesAsync(ct);return accessToken;}
        if(string.IsNullOrWhiteSpace(refreshToken))return accessToken;
        var refreshed=await _client.RefreshTokenAsync(refreshToken,ct);c.AccessTokenProtected=_protector.Protect(refreshed.AccessToken);c.RefreshTokenProtected=string.IsNullOrWhiteSpace(refreshed.RefreshToken)?c.RefreshTokenProtected:_protector.Protect(refreshed.RefreshToken);c.TokenExpiresAt=refreshed.ExpiresAt;c.Scope=refreshed.Scope??c.Scope;await db.SaveChangesAsync(ct);return refreshed.AccessToken;
    }
    private static string? Get(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():null;
}
