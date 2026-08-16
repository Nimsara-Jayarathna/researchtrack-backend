using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.JiraService.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack Jira Service");
builder.Services.AddJiraPersistence(builder.Configuration);

var app = builder.Build();
app.UseResearchTrackApi();
app.Run();

public partial class Program;
