using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.MutationTests.TestSupport;

namespace ResearchTrack.JiraService.MutationTests.Features;

public sealed class JiraWebhookMutationFinalHardeningTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer one.two")]
    [InlineData("Bearer !!!.!!!.!!!")]
    public async Task Receive_InvalidAuthorizationShapes_AreRejectedWithoutPersistenceOrScheduling(string? authorization)
    {
        var ct = CancellationToken.None;
        var setup = Setup();
        var accepted = await setup.Service.ReceiveAsync(authorization, "delivery", "{}", ct);
        Assert.False(accepted);
        Assert.Empty(setup.Scheduler.Requests);
        await using var db = setup.Factory.CreateDbContext();
        Assert.Empty(db.JiraWebhookEvents);
    }

    [Fact]
    public async Task Receive_RejectsWrongAlgorithmBadSignatureAndExpiredTokenIndependently()
    {
        var ct = CancellationToken.None;
        var setup = Setup();
        Assert.False(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret, alg:"HS512"), "d1", "{}", ct));
        Assert.False(await setup.Service.ReceiveAsync(Bearer("wrong-secret"), "d2", "{}", ct));
        Assert.False(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret, exp:DateTimeOffset.UtcNow.AddMinutes(-2)), "d3", "{}", ct));
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret, exp:DateTimeOffset.UtcNow.AddSeconds(-30)), "d4", "{}", ct));
    }

    [Fact]
    public async Task Receive_MatchedWebhookIdRoutesEvenWhenPayloadProjectIdentityDiffers()
    {
        var ct=CancellationToken.None; var setup=Setup(); var projectId=Guid.NewGuid();
        await SeedConnection(setup.Factory,projectId,"RT","10000","cloud-a",9001,"SYNCED",ct);
        var payload="""{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"123","key":"OTHER-9","fields":{"project":{"id":"99999","key":"OTHER"}}}}""";
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"delivery-1",payload,ct));
        var request=Assert.Single(setup.Scheduler.Requests);
        Assert.Equal(projectId,request.ProjectId); Assert.Equal("123",request.EntityId); Assert.Equal("WEBHOOK",request.Reason);
        await using var db=setup.Factory.CreateDbContext(); var stored=Assert.Single(db.JiraWebhookEvents);
        Assert.Equal(projectId,stored.ResearchProjectId); Assert.Equal("RECEIVED",stored.Status); Assert.Equal("OTHER-9",stored.IssueKey);
    }

    [Fact]
    public async Task Receive_ProjectIdentityWithoutMatchedIdRoutesOnlyWhenCloudIsUnambiguous()
    {
        var ct=CancellationToken.None; var setup=Setup(); var p1=Guid.NewGuid();
        await SeedConnection(setup.Factory,p1,"RT","10000","cloud-a",9001,"SYNCED",ct);
        var payload="""{"webhookEvent":"jira:issue_created","issue":{"id":"1","key":"RT-1","fields":{"project":{"id":"10000","key":"RT"}}}}""";
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"single-cloud",payload,ct));
        Assert.Equal(p1,Assert.Single(setup.Scheduler.Requests).ProjectId);

        var setup2=Setup(); await SeedConnection(setup2.Factory,Guid.NewGuid(),"RT","10000","cloud-a",1,"SYNCED",ct); await SeedConnection(setup2.Factory,Guid.NewGuid(),"RT","10000","cloud-b",2,"SYNCED",ct);
        Assert.True(await setup2.Service.ReceiveAsync(Bearer(setup2.Options.ClientSecret),"ambiguous",payload,ct));
        Assert.Empty(setup2.Scheduler.Requests);
        await using var db2=setup2.Factory.CreateDbContext(); var ignored=Assert.Single(db2.JiraWebhookEvents); Assert.Equal("IGNORED",ignored.Status); Assert.Null(ignored.ResearchProjectId);
    }

    [Fact]
    public async Task Receive_MatchedWebhookExpandsOnlySameCloudSourceCandidates()
    {
        var ct=CancellationToken.None; var setup=Setup(); var p1=Guid.NewGuid(); var p2=Guid.NewGuid(); var p3=Guid.NewGuid();
        await SeedConnection(setup.Factory,p1,"RT","10000","cloud-a",9001,"SYNCED",ct);
        await SeedConnection(setup.Factory,p2,"RT","10000","cloud-a",9002,"SYNCED",ct);
        await SeedConnection(setup.Factory,p3,"RT","10000","cloud-b",9003,"SYNCED",ct);
        var payload="""{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001,9001],"issue":{"id":"42","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"multi",payload,ct));
        Assert.Equal(2,setup.Scheduler.Requests.Count);
        Assert.Contains(setup.Scheduler.Requests,x=>x.ProjectId==p1); Assert.Contains(setup.Scheduler.Requests,x=>x.ProjectId==p2); Assert.DoesNotContain(setup.Scheduler.Requests,x=>x.ProjectId==p3);
        await using var db=setup.Factory.CreateDbContext(); var events=await db.JiraWebhookEvents.OrderBy(x=>x.ResearchProjectId).ToListAsync(ct); Assert.Equal(2,events.Count); Assert.All(events,x=>Assert.StartsWith("multi:",x.DeliveryId));
    }

    [Fact]
    public async Task Receive_InvalidAuthConnectionsNeverBecomeTargets()
    {
        var ct=CancellationToken.None; var setup=Setup(); var projectId=Guid.NewGuid();
        await SeedConnection(setup.Factory,projectId,"RT","10000","cloud",9001,"INVALID_AUTH",ct);
        var payload="""{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"42","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"invalid-auth",payload,ct));
        Assert.Empty(setup.Scheduler.Requests);
        await using var db=setup.Factory.CreateDbContext(); Assert.Equal("IGNORED",Assert.Single(db.JiraWebhookEvents).Status);
    }

    [Fact]
    public async Task Receive_DuplicateIgnoredUnmatchedDeliveryIsPersistedOnce()
    {
        var ct=CancellationToken.None; var setup=Setup(); var payload="""{"webhookEvent":"jira:issue_updated","issue":{"id":"1","key":"NONE-1"}}""";
        var auth=Bearer(setup.Options.ClientSecret);
        Assert.True(await setup.Service.ReceiveAsync(auth,null,payload,ct)); Assert.True(await setup.Service.ReceiveAsync(auth,null,payload,ct));
        await using var db=setup.Factory.CreateDbContext(); var row=Assert.Single(db.JiraWebhookEvents); Assert.Equal("IGNORED",row.Status); Assert.StartsWith("body-",row.DeliveryId); Assert.Equal("unknown",row.CloudId);
    }

    [Fact]
    public async Task Receive_LongDeliveryIdIsHashedAndBounded()
    {
        var ct=CancellationToken.None; var setup=Setup(); var payload="""{"webhookEvent":"jira:issue_updated","issue":{"id":"1","key":"NONE-1"}}""";
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),new string('x',250),payload,ct));
        await using var db=setup.Factory.CreateDbContext(); var row=Assert.Single(db.JiraWebhookEvents); Assert.StartsWith("delivery-",row.DeliveryId); Assert.True(row.DeliveryId.Length<200);
    }

    [Fact]
    public async Task Receive_ExistingReceivedDeliveryIsRequeuedButProcessedDeliveryIsNot()
    {
        var ct=CancellationToken.None; var setup=Setup(); var projectId=Guid.NewGuid(); await SeedConnection(setup.Factory,projectId,"RT","10000","cloud",9001,"SYNCED",ct);
        var payload="""{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"42","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";
        await SeedEvent(setup.Factory,"retry",projectId,"RECEIVED",ct);
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"retry",payload,ct)); Assert.Single(setup.Scheduler.Requests);
        setup.Scheduler.Requests.Clear();
        await using(var db=setup.Factory.CreateDbContext()){var row=await db.JiraWebhookEvents.SingleAsync(x=>x.DeliveryId=="retry",ct);row.Status="PROCESSED";row.ProcessedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(ct);}
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"retry",payload,ct)); Assert.Empty(setup.Scheduler.Requests);
    }

    [Fact]
    public async Task Receive_PreviouslyIgnoredSingleTargetIsPromotedAndRequeued()
    {
        var ct=CancellationToken.None; var setup=Setup(); var projectId=Guid.NewGuid(); await SeedConnection(setup.Factory,projectId,"RT","10000","cloud",9001,"SYNCED",ct);
        await SeedEvent(setup.Factory,"promote",null,"IGNORED",ct,lastError:"old",processedAt:DateTimeOffset.UtcNow);
        var payload="""{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"42","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"promote",payload,ct));
        Assert.Equal(projectId,Assert.Single(setup.Scheduler.Requests).ProjectId);
        await using var db=setup.Factory.CreateDbContext(); var row=await db.JiraWebhookEvents.SingleAsync(x=>x.DeliveryId=="promote",ct); Assert.Equal("RECEIVED",row.Status); Assert.Equal(projectId,row.ResearchProjectId); Assert.Null(row.LastError); Assert.Null(row.ProcessedAt);
    }

    [Fact]
    public async Task Receive_UsesIssueKeyFallbackForProjectKeyAndRootProjectFallbackForProjectId()
    {
        var ct=CancellationToken.None;
        var setup=Setup(); var p1=Guid.NewGuid(); await SeedConnection(setup.Factory,p1,"ABC","500","cloud",null,"SYNCED",ct);
        var byKey="""{"webhookEvent":"jira:issue_updated","issue":{"id":"1","key":"ABC-77"}}""";
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"fallback-key",byKey,ct)); Assert.Equal(p1,Assert.Single(setup.Scheduler.Requests).ProjectId);

        var setup2=Setup(); var p2=Guid.NewGuid(); await SeedConnection(setup2.Factory,p2,"XYZ","900","cloud",null,"SYNCED",ct);
        var byId="""{"webhookEvent":"jira:issue_updated","project":{"id":"900"},"issue":{"id":"2","key":"NOSEP"}}""";
        Assert.True(await setup2.Service.ReceiveAsync(Bearer(setup2.Options.ClientSecret),"fallback-id",byId,ct)); Assert.Equal(p2,Assert.Single(setup2.Scheduler.Requests).ProjectId);
    }

    [Fact]
    public async Task Receive_UpdatesConnectionHealthAndHonorsMinimumOneSecondCoalesce()
    {
        var ct=CancellationToken.None; var setup=Setup(coalesceSeconds:0); var projectId=Guid.NewGuid(); await SeedConnection(setup.Factory,projectId,"RT","10000","cloud",9001,"SYNCED",ct,webhookStatus:"DEGRADED");
        var before=DateTimeOffset.UtcNow; var payload="""{"issue_event_type_name":"issue_updated","matchedWebhookIds":[9001],"issue":{"id":"42","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";
        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret),"health",payload,ct));
        var request=Assert.Single(setup.Scheduler.Requests); Assert.True(request.AvailableAt>=before.AddMilliseconds(900));
        await using var db=setup.Factory.CreateDbContext(); var connection=await db.JiraConnections.SingleAsync(x=>x.ResearchProjectId==projectId,ct); Assert.Equal("ACTIVE",connection.WebhookStatus); Assert.NotNull(connection.LastWebhookAt); Assert.True(connection.UpdatedAt>=before);
    }

    private static SetupData Setup(int coalesceSeconds=3)
    {
        var factory=new TestJiraDbContextFactory(); var scheduler=new RecordingSyncScheduler(); var options=new JiraOptions{ClientSecret="webhook-secret",WebhookCoalesceSeconds=coalesceSeconds,TokenUrl="https://example.test/token",ApiBaseUrl="https://example.test",AccessibleResourcesUrl="https://example.test/resources"};
        var client=new AtlassianClient(new HttpClient(new NoopHandler()),options); var service=new JiraWebhookService(factory,client,new StubTokenProtector(),options,scheduler,NullLogger<JiraWebhookService>.Instance); return new(service,factory,scheduler,options);
    }

    private static async Task SeedConnection(TestJiraDbContextFactory factory,Guid projectId,string key,string jiraProjectId,string cloudId,long? webhookId,string syncStatus,CancellationToken ct,string webhookStatus="ACTIVE")
    {await using var db=factory.CreateDbContext();db.JiraConnections.Add(new JiraConnection{Id=Guid.NewGuid(),ResearchProjectId=projectId,CloudId=cloudId,WorkspaceName="Workspace",JiraProjectId=jiraProjectId,JiraProjectKey=key,JiraProjectName="ResearchTrack",AccessTokenProtected="protected:access",ConnectedByUserId=Guid.NewGuid(),ConnectedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow.AddHours(-1),SyncStatus=syncStatus,WebhookId=webhookId,WebhookStatus=webhookStatus});await db.SaveChangesAsync(ct);}
    private static async Task SeedEvent(TestJiraDbContextFactory factory,string deliveryId,Guid? projectId,string status,CancellationToken ct,string? lastError=null,DateTimeOffset? processedAt=null)
    {await using var db=factory.CreateDbContext();db.JiraWebhookEvents.Add(new JiraWebhookEvent{Id=Guid.NewGuid(),DeliveryId=deliveryId,CloudId=projectId.HasValue?"cloud":"unknown",ResearchProjectId=projectId,EventType="jira:issue_updated",PayloadJson="{}",ReceivedAt=DateTimeOffset.UtcNow,Status=status,LastError=lastError,ProcessedAt=processedAt});await db.SaveChangesAsync(ct);}

    private static string Bearer(string secret,string alg="HS256",DateTimeOffset? exp=null)
    {static string Encode(string json)=>WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));var header=Encode(JsonSerializer.Serialize(new{alg,typ="JWT"}));var payload=Encode(JsonSerializer.Serialize(new{exp=(exp??DateTimeOffset.UtcNow.AddMinutes(10)).ToUnixTimeSeconds()}));var input=header+"."+payload;using var hmac=new HMACSHA256(Encoding.UTF8.GetBytes(secret));return "Bearer "+input+"."+WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(input)));}

    private sealed record SetupData(JiraWebhookService Service,TestJiraDbContextFactory Factory,RecordingSyncScheduler Scheduler,JiraOptions Options);
    private sealed class NoopHandler:HttpMessageHandler{protected override Task<HttpResponseMessage>SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>throw new InvalidOperationException("HTTP should not be called in receive tests.");}
}
