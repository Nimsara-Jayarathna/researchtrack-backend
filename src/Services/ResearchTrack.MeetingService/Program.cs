using Prometheus;
using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack Meeting Service");
builder.Services.AddResearchTrackJwtAuthentication(builder.Configuration);
builder.Services.AddMeetingPersistence(builder.Configuration);

var app = builder.Build();
app.UseHttpMetrics();
app.UseResearchTrackApi();
app.MapMetrics();
app.Run();

public partial class Program;
