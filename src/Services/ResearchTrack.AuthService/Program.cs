using Prometheus;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.BuildingBlocks.Api.Security;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack Auth Service");
builder.Services.AddResearchTrackJwtAuthentication(builder.Configuration);
builder.Services.AddAuthPersistence(builder.Configuration);
builder.Services.AddAuthFeatures(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseHttpMetrics();
app.UseResearchTrackApi();
app.MapMetrics();
await app.RunAsync();

public partial class Program;
