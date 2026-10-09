using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Tests.TestSupport;

namespace ResearchTrack.JiraService.Tests.Services;

public sealed class JiraWebhookServiceMockInteractionTests
{
    [Fact]
    public async Task ReceiveAsync_ValidMatchingWebhook_QueuesExactlyOnceWithIssueId()
    {
        var setup = await SetupAsync();
        var payload = """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"123","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";

        var accepted = await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), "delivery-1", payload, TestContext.Current.CancellationToken);

        Assert.True(accepted);
        await setup.Scheduler.Received(1).RequestAsync(
            setup.ProjectId, "WEBHOOK", Arg.Any<DateTimeOffset>(), "123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAsync_InvalidBearer_DoesNotQueue()
    {
        var setup = await SetupAsync();

        var accepted = await setup.Service.ReceiveAsync("Bearer bad.token.value", "delivery-1", "{}", TestContext.Current.CancellationToken);

        Assert.False(accepted);
        await setup.Scheduler.DidNotReceiveWithAnyArgs().RequestAsync(default, default!, default, default, default);
    }

    [Fact]
    public async Task ReceiveAsync_DuplicateProcessedDelivery_DoesNotQueueAgain()
    {
        var setup = await SetupAsync();
        await using (var db = setup.Factory.CreateDbContext())
        {
            db.JiraWebhookEvents.Add(new JiraWebhookEvent
            {
                Id = Guid.NewGuid(), DeliveryId = "delivery-1", CloudId = "cloud", ResearchProjectId = setup.ProjectId,
                EventType = "jira:issue_updated", PayloadJson = "{}", ReceivedAt = DateTimeOffset.UtcNow,
                ProcessedAt = DateTimeOffset.UtcNow, Status = "PROCESSED"
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var payload = """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"123","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}}}""";

        var accepted = await setup.Service.ReceiveAsync(Bearer(setup.Options.ClientSecret), "delivery-1", payload, TestContext.Current.CancellationToken);

        Assert.True(accepted);
        await setup.Scheduler.DidNotReceiveWithAnyArgs().RequestAsync(default, default!, default, default, default);
    }

    private static async Task<Setup> SetupAsync()
    {
        var factory = new TestJiraDbContextFactory();
        var scheduler = Substitute.For<IJiraSyncScheduler>();
        var options = new JiraOptions
        {
            ClientSecret = "webhook-secret", WebhookCoalesceSeconds = 3,
            TokenUrl = "https://example.test/token", ApiBaseUrl = "https://example.test",
            AccessibleResourcesUrl = "https://example.test/resources"
        };
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.JiraConnections.Add(new JiraConnection
            {
                Id = Guid.NewGuid(), ResearchProjectId = projectId, CloudId = "cloud", WorkspaceName = "Workspace",
                JiraProjectId = "10000", JiraProjectKey = "RT", JiraProjectName = "ResearchTrack",
                AccessTokenProtected = "protected:access", ConnectedByUserId = Guid.NewGuid(), ConnectedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow, SyncStatus = "SYNCED", WebhookId = 9001, WebhookStatus = "ACTIVE"
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var client = new AtlassianClient(new HttpClient(new NoopHandler()), options);
        var service = new JiraWebhookService(factory, client, new StubTokenProtector(), options, scheduler, NullLogger<JiraWebhookService>.Instance);
        return new Setup(service, factory, scheduler, options, projectId);
    }

    private static string Bearer(string secret)
    {
        static string Encode(string json) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var header = Encode("""{"alg":"HS256","typ":"JWT"}""");
        var payload = Encode(JsonSerializer.Serialize(new { exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds() }));
        var input = header + "." + payload;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "Bearer " + input + "." + WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(input)));
    }

    private sealed record Setup(JiraWebhookService Service, TestJiraDbContextFactory Factory, IJiraSyncScheduler Scheduler, JiraOptions Options, Guid ProjectId);
    private sealed class NoopHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP should not be called in webhook receive tests.");
    }
}
