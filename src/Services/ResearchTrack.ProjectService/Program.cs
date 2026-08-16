using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.ProjectService.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack Project Service");
builder.Services.AddProjectPersistence(builder.Configuration);

var app = builder.Build();
app.UseResearchTrackApi();
app.Run();

public partial class Program;
