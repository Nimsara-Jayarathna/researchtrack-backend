using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Persistence;
using ResearchTrack.BuildingBlocks.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack Auth Service");
builder.Services.AddAuthPersistence(builder.Configuration);
builder.Services.AddAuthFeatures(builder.Configuration);

var app = builder.Build();
app.UseResearchTrackApi();
app.Run();

public partial class Program;
