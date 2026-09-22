using System.Text.Json;
using Microsoft.AspNetCore.Routing;

namespace ResearchTrack.BuildingBlocks.Api.RuntimeLogging;

public static class RuntimeLogEndpoints
{
    public const string RoutePrefix = "/_runtime-logs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapRuntimeLogViewer(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(RoutePrefix, () => Results.Content(RuntimeLogViewerAssets.Html, "text/html; charset=utf-8"))
            .ExcludeFromDescription();
        endpoints.MapGet($"{RoutePrefix}/assets/app.css", () => Results.Content(RuntimeLogViewerAssets.Css, "text/css; charset=utf-8"))
            .ExcludeFromDescription();
        endpoints.MapGet($"{RoutePrefix}/assets/app.js", () => Results.Content(RuntimeLogViewerAssets.JavaScript, "text/javascript; charset=utf-8"))
            .ExcludeFromDescription();
        endpoints.MapGet($"{RoutePrefix}/api/entries", (RuntimeLogStore store, int? limit) =>
                Results.Json(store.GetLatest(limit ?? 500), JsonOptions))
            .ExcludeFromDescription();
        endpoints.MapPost($"{RoutePrefix}/api/clear", (RuntimeLogStore store) =>
            {
                store.Clear();
                return Results.NoContent();
            })
            .ExcludeFromDescription();
        endpoints.MapGet($"{RoutePrefix}/api/stream", StreamLogsAsync)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task StreamLogsAsync(HttpContext context, RuntimeLogStore store)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        await context.Response.WriteAsync("retry: 2000\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);

        using var subscription = store.Subscribe();
        await foreach (var entry in subscription.Reader.ReadAllAsync(context.RequestAborted))
        {
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            await context.Response.WriteAsync($"id: {entry.Id}\ndata: {json}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
}
