using System.Net;
using System.Text.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.SubmissionService.Infrastructure;

public sealed class ProjectAuthorizationClient : IProjectAuthorizationClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProjectAuthorizationClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task EnsureCanAccessAsync(Guid projectId, CancellationToken cancellationToken) =>
        EnsureAsync($"api/v1/projects/{projectId}/authorization/access", "You do not have access to this project.", cancellationToken);

    public Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken) =>
        EnsureAsync($"api/v1/projects/{projectId}/authorization/manage", "Only the owning Supervisor can manage and review this project's submissions.", cancellationToken);

    public async Task<ProjectSubmissionContext> GetSubmissionContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/projects/{projectId}");
        ForwardAuthentication(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw DependencyFailure("Project Service is unavailable.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "You do not have access to this project.");
            if (!response.IsSuccessStatusCode)
                throw DependencyFailure("Unable to load project membership from Project Service.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw DependencyFailure("Project Service returned invalid project membership data.");

            ProjectStudentContext? leader = null;
            if (data.TryGetProperty("leader", out var leaderElement) && leaderElement.ValueKind == JsonValueKind.Object)
                leader = ParseStudent(leaderElement);

            var students = new List<ProjectStudentContext>();
            if (data.TryGetProperty("members", out var membersElement) && membersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in membersElement.EnumerateArray())
                {
                    if (!TryGetString(member, "memberRole", out var role) || !string.Equals(role, "STUDENT", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var student = ParseStudent(member);
                    if (student is not null) students.Add(student);
                }
            }

            return new ProjectSubmissionContext(leader, students);
        }
    }

    private async Task EnsureAsync(string relativePath, string forbiddenMessage, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        ForwardAuthentication(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw DependencyFailure("Project Service is unavailable.", exception);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, forbiddenMessage);
            throw DependencyFailure("Unable to verify project authorization with Project Service.");
        }
    }

    private static ProjectStudentContext? ParseStudent(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String || !Guid.TryParse(idElement.GetString(), out var id))
            return null;

        TryGetString(element, "firstName", out var firstName);
        TryGetString(element, "lastName", out var lastName);
        TryGetString(element, "email", out var email);
        TryGetString(element, "registrationNumber", out var registrationNumber);
        var displayName = $"{firstName} {lastName}".Trim();
        if (string.IsNullOrWhiteSpace(displayName)) displayName = email ?? "Student";
        return new ProjectStudentContext(id, displayName, email ?? string.Empty, registrationNumber);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null) return false;
        if (property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return true;
    }

    private void ForwardAuthentication(HttpRequestMessage request)
    {
        var incoming = _httpContextAccessor.HttpContext?.Request
            ?? throw DependencyFailure("Request authentication context is unavailable.");

        if (incoming.Headers.Authorization.Count > 0)
            request.Headers.TryAddWithoutValidation("Authorization", incoming.Headers.Authorization.ToArray());

        if (incoming.Cookies.TryGetValue(AuthSecurityConstants.AccessCookieName, out var accessToken)
            && !string.IsNullOrWhiteSpace(accessToken))
            request.Headers.TryAddWithoutValidation("Cookie", $"{AuthSecurityConstants.AccessCookieName}={accessToken}");
    }

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);
}
