using Microsoft.Data.Sqlite;
using System.Net.Http.Json;
using System.Net;
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
using ResearchTrack.JiraService.Persistence;
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


    [Fact]
    public async Task Receive_TrimsDeliveryIdAndCarriesIssueIdentityIntoEventAndScheduler()
    {
        var ct = CancellationToken.None;
        var setup = Setup(coalesceSeconds: 7);
        var projectId = Guid.NewGuid();
        await SeedConnection(setup.Factory, projectId, "RT", "10000", "cloud", 9001, "SYNCED", ct);
        var payload = """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"42","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";

        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), "  delivery-42  ", payload, ct));

        var request = Assert.Single(setup.Scheduler.Requests);
        Assert.Equal(projectId, request.ProjectId);
        Assert.Equal("WEBHOOK", request.Reason);
        Assert.Equal("42", request.EntityId);
        await using var db = setup.Factory.CreateDbContext();
        var row = Assert.Single(db.JiraWebhookEvents);
        Assert.Equal("delivery-42", row.DeliveryId);
        Assert.Equal("jira:issue_updated", row.EventType);
        Assert.Equal("42", row.JiraIssueId);
        Assert.Equal("RT-42", row.IssueKey);
        Assert.Equal("RECEIVED", row.Status);
        Assert.Equal("cloud", row.CloudId);
        Assert.Equal(projectId, row.ResearchProjectId);
    }

    [Fact]
    public async Task Receive_ExactlyTwoHundredCharacterDeliveryIdIsPreserved_ButTwoHundredOneIsHashed()
    {
        var ct = CancellationToken.None;
        var payload = """{"webhookEvent":"jira:issue_updated","issue":{"id":"1","key":"NONE-1"}}""";
        var setup200 = Setup();
        var id200 = new string('a', 200);
        Assert.True(await setup200.Service.ReceiveAsync(Bearer(setup200.Options.ClientSecret), id200, payload, ct));
        await using (var db = setup200.Factory.CreateDbContext())
            Assert.Equal(id200, Assert.Single(db.JiraWebhookEvents).DeliveryId);

        var setup201 = Setup();
        var id201 = new string('b', 201);
        Assert.True(await setup201.Service.ReceiveAsync(Bearer(setup201.Options.ClientSecret), id201, payload, ct));
        await using (var db = setup201.Factory.CreateDbContext())
        {
            var stored = Assert.Single(db.JiraWebhookEvents).DeliveryId;
            Assert.StartsWith("delivery-", stored);
            Assert.NotEqual(id201, stored);
        }
    }

    [Fact]
    public async Task Receive_MatchedWebhookIdsIgnoreDuplicatesAndNonNumbers()
    {
        var ct = CancellationToken.None;
        var setup = Setup();
        var projectId = Guid.NewGuid();
        await SeedConnection(setup.Factory, projectId, "RT", "10000", "cloud", 9001, "SYNCED", ct);
        var payload = """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001,9001,"9001",null],"issue":{"id":"5","key":"RT-5","fields":{"project":{"id":"10000","key":"RT"}}}}""";

        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), "dedupe-ids", payload, ct));

        Assert.Single(setup.Scheduler.Requests);
        await using var db = setup.Factory.CreateDbContext();
        Assert.Single(db.JiraWebhookEvents);
    }

    [Fact]
    public async Task Receive_IssueFieldsProjectTakesPriorityOverRootProjectAndIssueKeyFallback()
    {
        var ct = CancellationToken.None;
        var setup = Setup();
        var correct = Guid.NewGuid(); var wrong = Guid.NewGuid();
        await SeedConnection(setup.Factory, correct, "RIGHT", "100", "cloud", null, "SYNCED", ct);
        await SeedConnection(setup.Factory, wrong, "WRONG", "999", "cloud", null, "SYNCED", ct);
        var payload = """{"webhookEvent":"jira:issue_updated","project":{"id":"999","key":"WRONG"},"issue":{"id":"7","key":"FALLBACK-7","fields":{"project":{"id":"100","key":"RIGHT"}}}}""";

        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), "priority", payload, ct));

        Assert.Equal(correct, Assert.Single(setup.Scheduler.Requests).ProjectId);
    }

    [Fact]
    public async Task Receive_ExistingIgnoredEventIsNotPromotedWhenWebhookTargetsMultipleProjects()
    {
        var ct = CancellationToken.None;
        var setup = Setup();
        var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid();
        await SeedConnection(setup.Factory, p1, "RT", "10000", "cloud", 9001, "SYNCED", ct);
        await SeedConnection(setup.Factory, p2, "RT", "10000", "cloud", 9001, "SYNCED", ct);
        await SeedEvent(setup.Factory, "multi-promote", null, "IGNORED", ct, lastError: "old");
        var payload = """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"42","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";

        Assert.True(await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), "multi-promote", payload, ct));

        Assert.Equal(2, setup.Scheduler.Requests.Count);
        await using var db = setup.Factory.CreateDbContext();
        var old = await db.JiraWebhookEvents.SingleAsync(x => x.DeliveryId == "multi-promote", ct);
        Assert.Equal("IGNORED", old.Status);
        Assert.Null(old.ResearchProjectId);
        Assert.Equal(3, await db.JiraWebhookEvents.CountAsync(ct));
    }

    [Fact]
    public async Task Receive_ValidJwtWithoutExpiryIsAccepted_ButMalformedExpiryIsRejected()
    {
        var ct = CancellationToken.None;
        var setup = Setup();
        var payload = "{}";
        Assert.True(await setup.Service.ReceiveAsync(BearerWithoutExpiry(setup.Options.ClientSecret), "no-exp", payload, ct));
        Assert.False(await setup.Service.ReceiveAsync(BearerWithRawPayload(setup.Options.ClientSecret, "{\"exp\":\"not-a-number\"}"), "bad-exp", payload, ct));
        await using var db = setup.Factory.CreateDbContext();
        Assert.Single(db.JiraWebhookEvents);
    }

    [Fact]
    public async Task EnsureRegistered_InvalidAuthAndMissingWebhookConfigurationArePersistedWithoutHttp()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteJiraFactory.CreateAsync(ct);
        var handler = new RecordingHandler();
        var options = JiraOptionsFor("CHANGE_ME");
        var service = RegistrationService(factory, handler, options);
        var invalidProject = Guid.NewGuid(); var notConfiguredProject = Guid.NewGuid();
        await SeedConnection(factory, invalidProject, "A", "1", "cloud", null, "INVALID_AUTH", ct);
        await SeedConnection(factory, notConfiguredProject, "B", "2", "cloud", null, "SYNCED", ct);

        await service.EnsureRegisteredAsync(invalidProject, ct);
        await service.EnsureRegisteredAsync(notConfiguredProject, ct);

        await using var db = factory.CreateDbContext();
        Assert.Equal("REAUTH_REQUIRED", (await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == invalidProject, ct)).WebhookStatus);
        Assert.Equal("NOT_CONFIGURED", (await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == notConfiguredProject, ct)).WebhookStatus);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EnsureRegistered_ActiveWebhookBeyondSevenDaysSkipsHttpAndMarksActive()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteJiraFactory.CreateAsync(ct);
        var handler = new RecordingHandler();
        var options = JiraOptionsFor("https://example.test/jira/webhook");
        var projectId = Guid.NewGuid();
        await SeedConnection(factory, projectId, "RT", "10000", "cloud", 777, "SYNCED", ct, webhookStatus: "DEGRADED", webhookExpiresAt: DateTimeOffset.UtcNow.AddDays(8));
        var service = RegistrationService(factory, handler, options);

        await service.EnsureRegisteredAsync(projectId, ct);

        Assert.Empty(handler.Requests);
        await using var db = factory.CreateDbContext();
        Assert.Equal("ACTIVE", (await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == projectId, ct)).WebhookStatus);
    }

    [Fact]
    public async Task EnsureRegistered_ExpiringWebhookRefreshesAndPersistsReturnedExpiry()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteJiraFactory.CreateAsync(ct);
        var expectedExpiry = DateTimeOffset.UtcNow.AddDays(29);
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Contains("/webhook/refresh", request.RequestUri!.AbsoluteUri);
            return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new { expirationDate = expectedExpiry.ToString("O") }));
        });
        var options = JiraOptionsFor("https://example.test/jira/webhook");
        var projectId = Guid.NewGuid();
        await SeedConnection(factory, projectId, "RT", "10000", "cloud", 777, "SYNCED", ct, webhookExpiresAt: DateTimeOffset.UtcNow.AddDays(2));
        var service = RegistrationService(factory, handler, options);

        await service.EnsureRegisteredAsync(projectId, ct);

        Assert.Single(handler.Requests);
        await using var db = factory.CreateDbContext();
        var connection = await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == projectId, ct);
        Assert.Equal("ACTIVE", connection.WebhookStatus);
        Assert.NotNull(connection.WebhookExpiresAt);
        Assert.True((connection.WebhookExpiresAt!.Value - expectedExpiry).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task EnsureRegistered_RefreshFailureFallsBackToReplacementRegistration()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteJiraFactory.CreateAsync(ct);
        var calls = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            calls++;
            if (request.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
            Assert.Equal(HttpMethod.Post, request.Method);
            return JsonResponse(HttpStatusCode.OK, "{\"webhookRegistrationResult\":[{\"createdWebhookId\":12345}]}");
        });
        var options = JiraOptionsFor("https://example.test/jira/webhook");
        var projectId = Guid.NewGuid();
        await SeedConnection(factory, projectId, "RT", "10000", "cloud", 777, "SYNCED", ct, webhookExpiresAt: DateTimeOffset.UtcNow.AddDays(1));
        var service = RegistrationService(factory, handler, options);

        await service.EnsureRegisteredAsync(projectId, ct);

        Assert.Equal(2, calls);
        await using var db = factory.CreateDbContext();
        var connection = await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == projectId, ct);
        Assert.Equal(12345, connection.WebhookId);
        Assert.Equal("ACTIVE", connection.WebhookStatus);
        Assert.NotNull(connection.WebhookExpiresAt);
    }

    [Fact]
    public async Task EnsureRegistered_NewWebhookPostsProjectFilterAndPersistsIdentity()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteJiraFactory.CreateAsync(ct);
        var handler = new RecordingHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("/rest/api/3/webhook", request.RequestUri!.AbsoluteUri);
            Assert.Contains("project = RT", body);
            Assert.Contains("https://callback.example/jira", body);
            return JsonResponse(HttpStatusCode.OK, "{\"webhookRegistrationResult\":[{\"createdWebhookId\":555}]}");
        });
        var options = JiraOptionsFor("https://callback.example/jira");
        var projectId = Guid.NewGuid();
        await SeedConnection(factory, projectId, "RT", "10000", "cloud", null, "SYNCED", ct, webhookStatus: "NOT_REGISTERED");
        var service = RegistrationService(factory, handler, options);

        await service.EnsureRegisteredAsync(projectId, ct);

        Assert.Single(handler.Requests);
        await using var db = factory.CreateDbContext();
        var connection = await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == projectId, ct);
        Assert.Equal(555, connection.WebhookId);
        Assert.Equal("ACTIVE", connection.WebhookStatus);
        Assert.True(connection.WebhookExpiresAt > DateTimeOffset.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task EnsureRegistered_HttpFailureMarksWebhookDegradedWithoutInvalidatingAuth()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteJiraFactory.CreateAsync(ct);
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") });
        var options = JiraOptionsFor("https://callback.example/jira");
        var projectId = Guid.NewGuid();
        await SeedConnection(factory, projectId, "RT", "10000", "cloud", null, "SYNCED", ct);
        var service = RegistrationService(factory, handler, options);

        await service.EnsureRegisteredAsync(projectId, ct);

        await using var db = factory.CreateDbContext();
        var connection = await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == projectId, ct);
        Assert.Equal("DEGRADED", connection.WebhookStatus);
        Assert.Equal("SYNCED", connection.SyncStatus);
        Assert.NotEqual(default, connection.UpdatedAt);
    }

    [Fact]
    public async Task Remove_WithWebhookDeletesExactId_ButMissingWebhookPerformsNoHttp()
    {
        var ct = CancellationToken.None;
        await using var factory = await SqliteJiraFactory.CreateAsync(ct);
        var handler = new RecordingHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Contains("777", body);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var options = JiraOptionsFor("https://callback.example/jira");
        var withWebhook = Guid.NewGuid(); var withoutWebhook = Guid.NewGuid();
        await SeedConnection(factory, withWebhook, "RT", "1", "cloud", 777, "SYNCED", ct);
        await SeedConnection(factory, withoutWebhook, "RT2", "2", "cloud", null, "SYNCED", ct);
        var service = RegistrationService(factory, handler, options);

        await service.RemoveAsync(withWebhook, ct);
        await service.RemoveAsync(withoutWebhook, ct);

        Assert.Single(handler.Requests);
    }

    private static SetupData Setup(int coalesceSeconds=3)
    {
        var factory=new TestJiraDbContextFactory(); var scheduler=new RecordingSyncScheduler(); var options=new JiraOptions{ClientSecret="webhook-secret",WebhookCoalesceSeconds=coalesceSeconds,TokenUrl="https://example.test/token",ApiBaseUrl="https://example.test",AccessibleResourcesUrl="https://example.test/resources"};
        var client=new AtlassianClient(new HttpClient(new NoopHandler()),options); var service=new JiraWebhookService(factory,client,new StubTokenProtector(),options,scheduler,NullLogger<JiraWebhookService>.Instance); return new(service,factory,scheduler,options);
    }

    private static async Task SeedConnection(IDbContextFactory<JiraDbContext> factory,Guid projectId,string key,string jiraProjectId,string cloudId,long? webhookId,string syncStatus,CancellationToken ct,string webhookStatus="ACTIVE",DateTimeOffset? webhookExpiresAt=null)
    {await using var db=await factory.CreateDbContextAsync(ct);db.JiraConnections.Add(new JiraConnection{Id=Guid.NewGuid(),ResearchProjectId=projectId,CloudId=cloudId,WorkspaceName="Workspace",JiraProjectId=jiraProjectId,JiraProjectKey=key,JiraProjectName="ResearchTrack",AccessTokenProtected="protected:access",ConnectedByUserId=Guid.NewGuid(),ConnectedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow.AddHours(-1),SyncStatus=syncStatus,WebhookId=webhookId,WebhookExpiresAt=webhookExpiresAt,WebhookStatus=webhookStatus});await db.SaveChangesAsync(ct);}
    private static async Task SeedEvent(TestJiraDbContextFactory factory,string deliveryId,Guid? projectId,string status,CancellationToken ct,string? lastError=null,DateTimeOffset? processedAt=null)
    {await using var db=factory.CreateDbContext();db.JiraWebhookEvents.Add(new JiraWebhookEvent{Id=Guid.NewGuid(),DeliveryId=deliveryId,CloudId=projectId.HasValue?"cloud":"unknown",ResearchProjectId=projectId,EventType="jira:issue_updated",PayloadJson="{}",ReceivedAt=DateTimeOffset.UtcNow,Status=status,LastError=lastError,ProcessedAt=processedAt});await db.SaveChangesAsync(ct);}

    private static string Bearer(string secret,string alg="HS256",DateTimeOffset? exp=null)
    {static string Encode(string json)=>WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));var header=Encode(JsonSerializer.Serialize(new{alg,typ="JWT"}));var payload=Encode(JsonSerializer.Serialize(new{exp=(exp??DateTimeOffset.UtcNow.AddMinutes(10)).ToUnixTimeSeconds()}));var input=header+"."+payload;using var hmac=new HMACSHA256(Encoding.UTF8.GetBytes(secret));return "Bearer "+input+"."+WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(input)));}


    private static string BearerWithoutExpiry(string secret) => BearerWithRawPayload(secret, "{}");
    private static string BearerWithRawPayload(string secret, string rawPayload)
    {
        static string Encode(string json) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var header = Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = Encode(rawPayload);
        var input = header + "." + payload;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "Bearer " + input + "." + WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(input)));
    }

    private static JiraOptions JiraOptionsFor(string webhookUrl) => new()
    {
        ClientSecret = "webhook-secret",
        WebhookUrl = webhookUrl,
        WebhookCoalesceSeconds = 3,
        TokenUrl = "https://example.test/token",
        ApiBaseUrl = "https://example.test",
        AccessibleResourcesUrl = "https://example.test/resources"
    };

    private static JiraWebhookService RegistrationService(IDbContextFactory<JiraDbContext> factory, RecordingHandler handler, JiraOptions options) =>
        new(factory, new AtlassianClient(new HttpClient(handler), options), new StubTokenProtector(), options, new RecordingSyncScheduler(), NullLogger<JiraWebhookService>.Instance);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage>? _response;
        public List<(HttpMethod Method, string Uri, string Body)> Requests { get; } = [];
        public RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage>? response = null) => _response = response;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri?.AbsoluteUri ?? string.Empty, body));
            return _response?.Invoke(request, body) ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class SqliteJiraFactory : IDbContextFactory<JiraDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<JiraDbContext> _options;
        private SqliteJiraFactory(SqliteConnection connection)
        {
            _connection = connection;
            _options = new DbContextOptionsBuilder<JiraDbContext>().UseSqlite(connection).Options;
        }
        public static async Task<SqliteJiraFactory> CreateAsync(CancellationToken ct)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"jira-webhook-{Guid.NewGuid():N}.db");
            var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.CreateFunction<string, long, long>("GET_LOCK", (_, _) => 1);
            connection.CreateFunction<string, long>("RELEASE_LOCK", _ => 1);
            await connection.OpenAsync(ct);
            var factory = new SqliteJiraFactory(connection);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync(ct);
            return factory;
        }
        public JiraDbContext CreateDbContext() => new(_options);
        public Task<JiraDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed record SetupData(JiraWebhookService Service,TestJiraDbContextFactory Factory,RecordingSyncScheduler Scheduler,JiraOptions Options);
    private sealed class NoopHandler:HttpMessageHandler{protected override Task<HttpResponseMessage>SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>throw new InvalidOperationException("HTTP should not be called in receive tests.");}
}
