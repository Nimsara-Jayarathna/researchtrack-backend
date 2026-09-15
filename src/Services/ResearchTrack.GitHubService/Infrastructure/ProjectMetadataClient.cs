using System.Text.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class ProjectMetadataClient : IProjectMetadataClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProjectMetadataClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> GetTitleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/projects/{projectId}");
        ForwardAuthentication(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw DependencyFailure("Project Service is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure("Project Service did not respond in time.", exception);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
            }
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
            {
                throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Only the owning Supervisor can manage this project's GitHub integration.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw DependencyFailure("Unable to load project metadata from Project Service.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("title", out var titleElement)
                || titleElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(titleElement.GetString()))
            {
                throw DependencyFailure("Project Service returned invalid project metadata.");
            }

            return titleElement.GetString()!.Trim();
        }
    }

    private void ForwardAuthentication(HttpRequestMessage request)
    {
        var incoming = _httpContextAccessor.HttpContext?.Request
            ?? throw DependencyFailure("Request authentication context is unavailable.");

        if (incoming.Headers.Authorization.Count > 0)
        {
            request.Headers.TryAddWithoutValidation("Authorization", incoming.Headers.Authorization.ToArray());
        }
        if (incoming.Cookies.TryGetValue(AuthSecurityConstants.AccessCookieName, out var token)
            && !string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{AuthSecurityConstants.AccessCookieName}={token}");
        }
    }

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);
}
