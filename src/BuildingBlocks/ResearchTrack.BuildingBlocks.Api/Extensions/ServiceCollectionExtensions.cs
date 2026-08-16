using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;

namespace ResearchTrack.BuildingBlocks.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddResearchTrackApi(this IServiceCollection services, string serviceName)
    {
        services
            .AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(pair => pair.Value is { Errors.Count: > 0 })
                        .Select(pair => new ApiFieldError(
                            pair.Key,
                            pair.Value!.Errors
                                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                                    ? "The supplied value is invalid."
                                    : error.ErrorMessage)
                                .ToArray()))
                        .ToArray();

                    var response = ApiResponse<object>.Fail(
                        new ApiError(ErrorCodes.ValidationError, "Validation failed.", errors),
                        new ApiMeta(context.HttpContext.TraceIdentifier, DateTimeOffset.UtcNow));

                    return new BadRequestObjectResult(response);
                };
            });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = serviceName,
                Version = "v1",
                Description = "ResearchTrack service API. Business endpoints are added by sprint feature implementations."
            });
        });
        services.AddHealthChecks();

        return services;
    }
}
