using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.MutationTests.TestSupport;

namespace ResearchTrack.JiraService.MutationTests.Features;

public sealed class JiraWebhookServiceTests
{
    [Fact]
    public async Task ReceiveAsync_MissingBearer_ReturnsFalseWithoutQueueing()
    {
        var setup = Setup();

        var accepted = await setup.Service.ReceiveAsync(null, "delivery-1", "{}", CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(setup.Scheduler.Requests);
    }

    [Fact]
    public async Task ReceiveAsync_InvalidSignature_ReturnsFalse()
    {
        var setup = Setup();

        var accepted = await setup.Service.ReceiveAsync("Bearer abc.def.ghi", "delivery-1", "{}", CancellationToken.None);

        Assert.False(accepted);
    }

    [Fact]
    public async Task ReceiveAsync_ValidWebhookMatchingProject_StoresEventAndQueuesSync()
    {
        var setup = Setup();
        var projectId = Guid.NewGuid();
        await SeedConnectionAsync(setup.Factory, projectId, "RT", "10000", webhookId: 9001);
        var payload = """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"123","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";

        var accepted = await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), "delivery-1", payload, CancellationToken.None);

        Assert.True(accepted);
        var request = Assert.Single(setup.Scheduler.Requests);
        Assert.Equal(projectId, request.ProjectId);
        Assert.Equal("WEBHOOK", request.Reason);
        Assert.Equal("123", request.EntityId);
        await using var db = setup.Factory.CreateDbContext();
        var stored = Assert.Single(db.JiraWebhookEvents);
        Assert.Equal("RECEIVED", stored.Status);
        Assert.Equal("RT-42", stored.IssueKey);
    }

    [Fact]
    public async Task ReceiveAsync_DuplicateProcessedDelivery_DoesNotQueueAnotherSync()
    {
        var setup = Setup();
        var projectId = Guid.NewGuid();
        await SeedConnectionAsync(setup.Factory, projectId, "RT", "10000", webhookId: 9001);
        await using (var db = setup.Factory.CreateDbContext())
        {
            db.JiraWebhookEvents.Add(new JiraWebhookEvent
            {
                Id = Guid.NewGuid(), DeliveryId = "delivery-1", CloudId = "cloud", ResearchProjectId = projectId,
                EventType = "jira:issue_updated", PayloadJson = "{}", ReceivedAt = DateTimeOffset.UtcNow,
                ProcessedAt = DateTimeOffset.UtcNow, Status = "PROCESSED"
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        var payload = """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"123","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";

        var accepted = await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), "delivery-1", payload, CancellationToken.None);

        Assert.True(accepted);
        Assert.Empty(setup.Scheduler.Requests);
    }

    [Fact]
    public async Task ReceiveAsync_UnmatchedAuthenticatedWebhook_IsStoredAsIgnored()
    {
        var setup = Setup();
        var payload = """{"webhookEvent":"jira:issue_updated","issue":{"id":"123","key":"OTHER-1","fields":{"project":{"id":"20000","key":"OTHER"}}}}""";

        var accepted = await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), null, payload, CancellationToken.None);

        Assert.True(accepted);
        await using var db = setup.Factory.CreateDbContext();
        var stored = Assert.Single(db.JiraWebhookEvents);
        Assert.Equal("IGNORED", stored.Status);
        Assert.StartsWith("body-", stored.DeliveryId);
    }

    private static (JiraWebhookService Service, TestJiraDbContextFactory Factory, RecordingSyncScheduler Scheduler, JiraOptions Options) Setup()
    {
        var factory = new TestJiraDbContextFactory();
        var scheduler = new RecordingSyncScheduler();
        var options = new JiraOptions
        {
            ClientSecret = "webhook-secret",
            WebhookCoalesceSeconds = 3,
            TokenUrl = "https://example.test/token",
            ApiBaseUrl = "https://example.test",
            AccessibleResourcesUrl = "https://example.test/resources"
        };
        var client = new AtlassianClient(new HttpClient(new NoopHandler()), options);
        var service = new JiraWebhookService(factory, client, new StubTokenProtector(), options, scheduler, NullLogger<JiraWebhookService>.Instance);
        return (service, factory, scheduler, options);
    }

    private static async Task SeedConnectionAsync(TestJiraDbContextFactory factory, Guid projectId, string key, string jiraProjectId, long? webhookId)
    {
        await using var db = factory.CreateDbContext();
        db.JiraConnections.Add(new JiraConnection
        {
            Id = Guid.NewGuid(), ResearchProjectId = projectId, CloudId = "cloud", WorkspaceName = "Workspace",
            JiraProjectId = jiraProjectId, JiraProjectKey = key, JiraProjectName = "ResearchTrack",
            AccessTokenProtected = "protected:access", ConnectedByUserId = Guid.NewGuid(), ConnectedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow, SyncStatus = "SYNCED", WebhookId = webhookId, WebhookStatus = "ACTIVE"
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string Bearer(string secret)
    {
        static string Encode(string json) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var header = Encode("""{"alg":"HS256","typ":"JWT"}""");
        var payload = Encode(JsonSerializer.Serialize(new { exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds() }));
        var input = header + "." + payload;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(input)));
        return "Bearer " + input + "." + signature;
    }

    private sealed class NoopHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP should not be called in this test.");
    }
}
