using Microsoft.AspNetCore.Http;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Infrastructure.GitHubApp;

public sealed class GitHubAppPermissionRequirementsTests
{
    [Fact]
    public void EnsureInstallationActive_rejects_suspended_installation()
    {
        var installation = new GitHubInstallationInfo(
            10,
            "openai",
            "ORG",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Suspended: true);

        var exception = Assert.Throws<ResearchTrack.BuildingBlocks.Api.Exceptions.ApiException>(() =>
            GitHubAppPermissionRequirements.EnsureInstallationActive(installation));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public void Read_permissions_allow_synchronization()
    {
        var installation = new GitHubInstallationInfo(
            1,
            "org",
            "ORG",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["metadata"] = "read",
                ["contents"] = "read",
                ["pull_requests"] = "read"
            });

        GitHubAppPermissionRequirements.EnsureSynchronizationReadPermissions(installation);
    }

    [Fact]
    public void Missing_contents_permission_is_actionable_conflict()
    {
        var installation = new GitHubInstallationInfo(
            1,
            "org",
            "ORG",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["metadata"] = "read",
                ["pull_requests"] = "read"
            });

        var exception = Assert.Throws<ApiException>(() =>
            GitHubAppPermissionRequirements.EnsureSynchronizationReadPermissions(installation));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Contains("Contents: Read-only", exception.Message, StringComparison.Ordinal);
    }
}
