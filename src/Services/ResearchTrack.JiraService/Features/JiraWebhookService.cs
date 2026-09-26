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
            if(!ValidateBearer(authorization))
            {
                JiraOperationalMetrics.WebhookEvents.WithLabels("unknown", "rejected_auth").Inc();
                return false;
            }

            using var doc=JsonDocument.Parse(payload);
            var root=doc.RootElement;
            var eventType=Get(root,"webhookEvent")??Get(root,"issue_event_type_name")??"unknown";
            var metricEvent=JiraOperationalMetrics.Event(eventType);
            var issue=root.TryGetProperty("issue",out var i)?i:default;
            var jiraIssueId=issue.ValueKind==JsonValueKind.Object?Get(issue,"id"):null;
            var issueKey=issue.ValueKind==JsonValueKind.Object?Get(issue,"key"):null;
            var projectKey=ExtractProjectKey(root,issue,issueKey);
            var jiraProjectId=ExtractProjectId(root,issue);
            var matchedIds=ExtractMatchedWebhookIds(root);
            var baseDeliveryId=NormalizeDeliveryId(deliveryId,payload);

            await using var db=await _factory.CreateDbContextAsync(ct);

            var matchedConnections=matchedIds.Count==0
                ? new List<JiraConnection>()
                : await db.JiraConnections
                    .Where(x=>x.SyncStatus!="INVALID_AUTH"&&x.WebhookId.HasValue&&matchedIds.Contains(x.WebhookId.Value))
                    .ToListAsync(ct);

            var sourceCandidates=new List<JiraConnection>();
            if(!string.IsNullOrWhiteSpace(projectKey)||!string.IsNullOrWhiteSpace(jiraProjectId))
            {
                sourceCandidates=await db.JiraConnections
                    .Where(x=>x.SyncStatus!="INVALID_AUTH"&&
                        ((!string.IsNullOrWhiteSpace(projectKey)&&x.JiraProjectKey==projectKey)||
                         (!string.IsNullOrWhiteSpace(jiraProjectId)&&x.JiraProjectId==jiraProjectId)))
                    .ToListAsync(ct);
            }

            var targets=new Dictionary<Guid,JiraConnection>();
            foreach(var connection in matchedConnections)
                targets[connection.ResearchProjectId]=connection;

            if(matchedConnections.Count>0)
            {
                var matchedClouds=matchedConnections.Select(x=>x.CloudId).ToHashSet(StringComparer.Ordinal);
                foreach(var connection in sourceCandidates.Where(x=>matchedClouds.Contains(x.CloudId)))
                    targets[connection.ResearchProjectId]=connection;
            }
            else if(sourceCandidates.Count>0)
            {
                var cloudCount=sourceCandidates.Select(x=>x.CloudId).Distinct(StringComparer.Ordinal).Count();
                if(cloudCount==1)
                {
                    foreach(var connection in sourceCandidates)
                        targets[connection.ResearchProjectId]=connection;
                }
                else
                {
                    _logger.LogWarning(
                        "Jira webhook could not be routed safely because project identity matched multiple Jira clouds. EventType={EventType} IssueKey={IssueKey} ProjectKey={ProjectKey} JiraProjectId={JiraProjectId} MatchedWebhookIds={MatchedWebhookIds} DeliveryId={DeliveryId}",
                        eventType,issueKey,projectKey,jiraProjectId,string.Join(",",matchedIds),baseDeliveryId);
                }
            }

            var now=DateTimeOffset.UtcNow;
            if(targets.Count==0)
            {
                if(!await db.JiraWebhookEvents.AnyAsync(x=>x.DeliveryId==baseDeliveryId,ct))
                {
                    db.JiraWebhookEvents.Add(new JiraWebhookEvent
                    {
                        Id=Guid.NewGuid(),
                        DeliveryId=baseDeliveryId,
                        CloudId="unknown",
                        ResearchProjectId=null,
                        EventType=eventType,
                        JiraIssueId=jiraIssueId,
                        IssueKey=issueKey,
                        PayloadJson=payload,
                        ReceivedAt=now,
                        Status="IGNORED",
                        LastError="Webhook did not match an active Jira connection."
                    });
                    await db.SaveChangesAsync(ct);
                }

                JiraOperationalMetrics.WebhookEvents.WithLabels(metricEvent,"ignored_unmatched").Inc();
                _logger.LogWarning(
                    "Jira webhook was authenticated but did not match an active connection. EventType={EventType} IssueKey={IssueKey} ProjectKey={ProjectKey} JiraProjectId={JiraProjectId} MatchedWebhookIds={MatchedWebhookIds} DeliveryId={DeliveryId}",
                    eventType,issueKey,projectKey,jiraProjectId,string.Join(",",matchedIds),baseDeliveryId);
                return true;
            }

            var targetList=targets.Values.OrderBy(x=>x.ResearchProjectId).ToList();
            var projectsToSchedule=new List<Guid>();
            foreach(var connection in targetList)
            {
                var eventDeliveryId=BuildProjectDeliveryId(baseDeliveryId,connection.ResearchProjectId,targetList.Count);
                var existing=await db.JiraWebhookEvents.SingleOrDefaultAsync(x=>x.DeliveryId==eventDeliveryId,ct);
                if(existing is null)
                {
                    db.JiraWebhookEvents.Add(new JiraWebhookEvent
                    {
                        Id=Guid.NewGuid(),
                        DeliveryId=eventDeliveryId,
                        CloudId=connection.CloudId,
                        ResearchProjectId=connection.ResearchProjectId,
                        EventType=eventType,
                        JiraIssueId=jiraIssueId,
                        IssueKey=issueKey,
                        PayloadJson=payload,
                        ReceivedAt=now,
                        Status="RECEIVED"
                    });
                    projectsToSchedule.Add(connection.ResearchProjectId);
                }
                else if(existing.Status=="RECEIVED")
                {
                    // A previous delivery may have been persisted immediately before scheduling
                    // failed. Requeueing RECEIVED events makes an Atlassian retry self-healing.
                    projectsToSchedule.Add(connection.ResearchProjectId);
                }
                else if(existing.Status=="IGNORED"&&targetList.Count==1)
                {
                    // A connection can be established between the first delivery and Atlassian's
                    // retry. Promote that previously unmatched delivery instead of permanently
                    // deduplicating it as IGNORED.
                    existing.CloudId=connection.CloudId;
                    existing.ResearchProjectId=connection.ResearchProjectId;
                    existing.Status="RECEIVED";
                    existing.LastError=null;
                    existing.ProcessedAt=null;
                    projectsToSchedule.Add(connection.ResearchProjectId);
                }

                connection.LastWebhookAt=now;
                connection.WebhookStatus="ACTIVE";
                connection.UpdatedAt=now;
            }

            await db.SaveChangesAsync(ct);

            foreach(var projectId in projectsToSchedule.Distinct())
                await _scheduler.RequestAsync(projectId,"WEBHOOK",now.AddSeconds(Math.Max(1,_options.WebhookCoalesceSeconds)),jiraIssueId,ct);

            JiraOperationalMetrics.WebhookEvents.WithLabels(metricEvent,"accepted").Inc(targetList.Count);
            _logger.LogInformation(
                "Jira webhook matched {ConnectionCount} active connection(s) and queued {QueuedCount} project sync(s). EventType={EventType} IssueKey={IssueKey} ProjectKey={ProjectKey} MatchedWebhookIds={MatchedWebhookIds} Projects={Projects}",
                targetList.Count,projectsToSchedule.Distinct().Count(),eventType,issueKey,projectKey,string.Join(",",matchedIds),string.Join(",",targetList.Select(x=>x.ResearchProjectId)));
            return true;
        }
        finally
        {
            JiraOperationalMetrics.WebhookReceiveDuration.Observe(receiveTimer.Elapsed.TotalSeconds);
        }
    }

    private static List<long> ExtractMatchedWebhookIds(JsonElement root)
    {
        var ids=new List<long>();
        if(root.TryGetProperty("matchedWebhookIds",out var matched)&&matched.ValueKind==JsonValueKind.Array)
            foreach(var item in matched.EnumerateArray())
                if(item.TryGetInt64(out var value)&&!ids.Contains(value))ids.Add(value);
        return ids;
    }

    private static string? ExtractProjectKey(JsonElement root,JsonElement issue,string? issueKey)
    {
        if(issue.ValueKind==JsonValueKind.Object&&issue.TryGetProperty("fields",out var fields)&&fields.ValueKind==JsonValueKind.Object&&fields.TryGetProperty("project",out var project))
        {
            var key=Get(project,"key");
            if(!string.IsNullOrWhiteSpace(key))return key;
        }

        if(root.TryGetProperty("project",out var rootProject))
        {
            var key=Get(rootProject,"key");
            if(!string.IsNullOrWhiteSpace(key))return key;
        }

        if(!string.IsNullOrWhiteSpace(issueKey))
        {
            var separator=issueKey.LastIndexOf('-');
            if(separator>0)return issueKey[..separator];
        }
        return null;
    }

    private static string? ExtractProjectId(JsonElement root,JsonElement issue)
    {
        if(issue.ValueKind==JsonValueKind.Object&&issue.TryGetProperty("fields",out var fields)&&fields.ValueKind==JsonValueKind.Object&&fields.TryGetProperty("project",out var project))
        {
            var id=Get(project,"id");
            if(!string.IsNullOrWhiteSpace(id))return id;
        }
        if(root.TryGetProperty("project",out var rootProject))return Get(rootProject,"id");
        return null;
    }

    private static string NormalizeDeliveryId(string? deliveryId,string payload)
    {
        var value=string.IsNullOrWhiteSpace(deliveryId)
            ? "body-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            : deliveryId.Trim();
        if(value.Length<=200)return value;
        return "delivery-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string BuildProjectDeliveryId(string baseDeliveryId,Guid projectId,int targetCount)
        => targetCount<=1?baseDeliveryId:$"{baseDeliveryId}:{projectId:N}";

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
