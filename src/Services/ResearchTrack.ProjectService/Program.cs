using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Persistence;
using ResearchTrack.ProjectService.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack Project Service");
builder.Services.AddResearchTrackJwtAuthentication(builder.Configuration);
builder.Services.AddProjectPersistence(builder.Configuration);
builder.Services.AddProjectFeatures();

var app = builder.Build();
app.UseResearchTrackApi();
app.Run();

public partial class Program;
