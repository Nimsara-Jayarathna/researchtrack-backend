using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.AuthService.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack Auth Service");
builder.Services.AddAuthPersistence(builder.Configuration);

var app = builder.Build();
app.UseResearchTrackApi();
app.Run();

public partial class Program;
