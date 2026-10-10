using Prometheus;
using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.GitHubService.Extensions;
using ResearchTrack.GitHubService.Persistence;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.GitHubService.Features.Webhooks;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Services.AddResearchTrackKafkaConfiguration(builder.Configuration, builder.Environment.IsProduction(), "github");

builder.Services.AddResearchTrackApi("ResearchTrack GitHub Service");
builder.Services.AddResearchTrackJwtAuthentication(builder.Configuration);
builder.Services.AddGitHubPersistence(builder.Configuration);
builder.Services.AddGitHubFeatures(builder.Configuration);
builder.Services.AddWebhookKafkaMessaging<GitHubDbContext, GitHubKafkaEventHandler>(builder.Configuration, "github");

var app = builder.Build();
app.UseHttpMetrics();
app.UseResearchTrackApi();
app.MapMetrics();
app.Run();

public partial class Program;
