using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ResearchTrack.BuildingBlocks.Api.Health;
using ResearchTrack.BuildingBlocks.Api.Infrastructure;
using ResearchTrack.BuildingBlocks.Api.Middleware;

namespace ResearchTrack.BuildingBlocks.Api.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseResearchTrackApi(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        app.UseStatusCodePages(async statusCodeContext =>
        {
            if (!statusCodeContext.HttpContext.Response.HasStarted)
            {
                await ApiErrorResponseWriter.WriteStatusCodeAsync(statusCodeContext.HttpContext);
            }
        });


        app.UseAuthentication();
        app.UseAuthorization();


        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "ResearchTrack API v1");
                options.DisplayRequestDuration();
            });
        }

        app.MapControllers();
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.WriteAsync
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = HealthResponseWriter.WriteAsync
        });

        return app;
    }
}
