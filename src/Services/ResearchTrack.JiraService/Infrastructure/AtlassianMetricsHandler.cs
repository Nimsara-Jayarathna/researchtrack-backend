using System.Diagnostics;
using Prometheus;

namespace ResearchTrack.JiraService.Infrastructure;

internal sealed class AtlassianMetricsHandler : DelegatingHandler
{
    private static readonly Counter Requests = Metrics.CreateCounter(
        "jira_atlassian_api_requests_total",
        "Outbound Atlassian API requests by bounded operation and outcome.",
        new CounterConfiguration { LabelNames = ["operation", "outcome"] });

    private static readonly Histogram Duration = Metrics.CreateHistogram(
        "jira_atlassian_api_request_duration_seconds",
        "Outbound Atlassian API request duration by bounded operation.",
        new HistogramConfiguration
        {
            LabelNames = ["operation"],
            Buckets = Histogram.ExponentialBuckets(0.05, 2, 12)
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var operation = Operation(request);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            Requests.WithLabels(operation, Outcome(response)).Inc();
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Requests.WithLabels(operation, "timeout").Inc();
            throw;
        }
        catch
        {
            Requests.WithLabels(operation, "transport_error").Inc();
            throw;
        }
        finally
        {
            Duration.WithLabels(operation).Observe(stopwatch.Elapsed.TotalSeconds);
        }
    }

    private static string Outcome(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        return code switch
        {
            >= 200 and < 300 => "success",
            401 or 403 => "authorization_error",
            429 => "rate_limited",
            >= 500 => "server_error",
            _ => "client_error"
        };
    }

    private static string Operation(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (path.Contains("/oauth/token", StringComparison.OrdinalIgnoreCase)) return "oauth_token";
        if (path.Contains("/oauth/token/accessible-resources", StringComparison.OrdinalIgnoreCase)) return "accessible_resources";
        if (path.Contains("/project/search", StringComparison.OrdinalIgnoreCase)) return "projects";
        if (path.Contains("/board", StringComparison.OrdinalIgnoreCase) && path.Contains("/sprint", StringComparison.OrdinalIgnoreCase)) return "sprints";
        if (path.Contains("/board", StringComparison.OrdinalIgnoreCase)) return "boards";
        if (path.EndsWith("/field", StringComparison.OrdinalIgnoreCase)) return "fields";
        if (path.Contains("/search/jql", StringComparison.OrdinalIgnoreCase)) return "issue_search";
        if (path.Contains("/webhook/refresh", StringComparison.OrdinalIgnoreCase)) return "webhook_refresh";
        if (path.EndsWith("/webhook", StringComparison.OrdinalIgnoreCase)) return request.Method == HttpMethod.Delete ? "webhook_delete" : "webhook_register";
        return "other";
    }
}
