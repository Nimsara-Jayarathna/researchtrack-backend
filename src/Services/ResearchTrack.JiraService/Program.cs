
using Prometheus;
using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.JiraService.Extensions;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
    options.AddServerHeader = false
);

// Kafka runtime configuration.
// Validates production-specific Kafka requirements.
builder.Services.AddResearchTrackKafkaConfiguration(
    builder.Configuration,
    builder.Environment.IsProduction(),
    "jira"
);

// Existing API and authentication configuration.
builder.Services.AddResearchTrackApi("ResearchTrack Jira Service");

builder.Services.AddResearchTrackJwtAuthentication(
    builder.Configuration
);

// Existing Jira database and feature registrations.
builder.Services.AddJiraPersistence(builder.Configuration);

builder.Services.AddJiraFeatures(builder.Configuration);

// Kafka producer, outbox publisher and event consumer registration.
builder.Services.AddWebhookKafkaMessaging<JiraDbContext, JiraKafkaEventHandler>(
    builder.Configuration,
    "jira"
);

var app = builder.Build();

// Existing Prometheus monitoring.
app.UseHttpMetrics();

// Existing ResearchTrack API middleware and endpoints.
app.UseResearchTrackApi();

app.MapMetrics();

app.Run();

// Required for WebApplicationFactory integration tests.
public partial class Program;
