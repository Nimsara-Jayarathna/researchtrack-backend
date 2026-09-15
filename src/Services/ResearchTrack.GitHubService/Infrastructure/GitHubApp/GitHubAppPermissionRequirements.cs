using Microsoft.AspNetCore.Http;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public static class GitHubAppPermissionRequirements
{
    public const string Contents = "contents";
    public const string PullRequests = "pull_requests";

    public static void EnsureInstallationActive(GitHubInstallationInfo installation)
    {
        if (!installation.Suspended)
        {
            return;
        }

        throw new ApiException(
            StatusCodes.Status409Conflict,
            ErrorCodes.Conflict,
            "The ResearchTrack GitHub App installation is suspended. Unsuspend the installation in GitHub before refreshing or synchronizing repositories.");
    }

    public static void EnsureSynchronizationReadPermissions(GitHubInstallationInfo installation)
    {
        // Test doubles created before permission-aware installation metadata may
        // omit the dictionary. Production GitHubAppClient always supplies it.
        if (installation.Permissions is null)
        {
            return;
        }

        var missing = new List<string>();
        if (!HasReadOrWrite(installation.Permissions, Contents))
        {
            missing.Add("Contents: Read-only");
        }
        if (!HasReadOrWrite(installation.Permissions, PullRequests))
        {
            missing.Add("Pull requests: Read-only");
        }

        if (missing.Count == 0)
        {
            return;
        }

        throw new ApiException(
            StatusCodes.Status409Conflict,
            ErrorCodes.Conflict,
            $"The ResearchTrack GitHub App installation is missing required synchronization permission(s): {string.Join(", ", missing)}. Update the GitHub App repository permissions, approve the permission update in GitHub, then reconnect or retry synchronization.");
    }

    private static bool HasReadOrWrite(
        IReadOnlyDictionary<string, string> permissions,
        string name)
    {
        if (!permissions.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("read", StringComparison.OrdinalIgnoreCase)
            || value.Equals("write", StringComparison.OrdinalIgnoreCase);
    }
}
