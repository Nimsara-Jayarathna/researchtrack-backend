using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.GitHubService.Extensions;
using ResearchTrack.GitHubService.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack GitHub Service");
builder.Services.AddResearchTrackJwtAuthentication(builder.Configuration);
builder.Services.AddGitHubPersistence(builder.Configuration);
builder.Services.AddGitHubFeatures(builder.Configuration);

var app = builder.Build();
app.UseResearchTrackApi();
app.Run();

public partial class Program;
