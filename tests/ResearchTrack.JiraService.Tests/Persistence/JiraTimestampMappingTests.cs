using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Tests.Persistence;

public sealed class JiraTimestampMappingTests
{
    [Theory]
    [InlineData(typeof(JiraWebhookEvent), nameof(JiraWebhookEvent.ReceivedAt))]
    [InlineData(typeof(JiraWebhookEvent), nameof(JiraWebhookEvent.ProcessedAt))]
    [InlineData(typeof(JiraSyncJob), nameof(JiraSyncJob.StartedAt))]
    [InlineData(typeof(JiraSyncJob), nameof(JiraSyncJob.RequestedAt))]
    [InlineData(typeof(JiraSyncJob), nameof(JiraSyncJob.CompletedAt))]
    [InlineData(typeof(JiraSyncJob), nameof(JiraSyncJob.AvailableAt))]
    [InlineData(typeof(JiraConnection), nameof(JiraConnection.WebhookExpiresAt))]
    [InlineData(typeof(JiraConnection), nameof(JiraConnection.LastWebhookAt))]
    [InlineData(typeof(JiraConnection), nameof(JiraConnection.LastReconciledAt))]
    public void KafkaIdentityAndSchedulingTimestampsRetainMySqlMicroseconds(Type entity, string property)
    {
        // Inspect the real MySQL model without opening a connection. SQLite's
        // timestamp storage cannot catch this provider precision regression.
        var options = new DbContextOptionsBuilder<JiraDbContext>()
            .UseMySQL("Server=127.0.0.1;Database=researchtrack_kafka_jira;User=fixture;Password=unused;SslMode=Required")
            .Options;
        using var db = new JiraDbContext(options);
        Assert.Equal("datetime(6)", db.Model.FindEntityType(entity)!.FindProperty(property)!.GetColumnType());
    }
}
