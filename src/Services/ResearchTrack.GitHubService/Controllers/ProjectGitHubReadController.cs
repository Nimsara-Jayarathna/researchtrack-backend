using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features;

namespace ResearchTrack.GitHubService.Controllers;

/// <summary>
/// Project-scoped, read-only GitHub evidence available to any authenticated
/// ResearchTrack member who can access the project. GitHub integration
/// management remains on supervisor-only endpoints.
/// </summary>
[Route("api/v1/projects/{projectId:guid}/github")]
[Authorize(Policy = AuthSecurityConstants.Policies.Authenticated)]
public sealed class ProjectGitHubReadController : ApiControllerBase
{
    private readonly IGitHubEvidenceQueryService _evidenceQueryService;
    private readonly IGitHubDashboardQueryService _dashboardQueryService;

    public ProjectGitHubReadController(
        IGitHubEvidenceQueryService evidenceQueryService,
        IGitHubDashboardQueryService dashboardQueryService)
    {
        _evidenceQueryService = evidenceQueryService;
        _dashboardQueryService = dashboardQueryService;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<GitHubDashboardResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GitHubDashboardResponse>>> GetDashboard(
        Guid projectId,
        [FromQuery] Guid? linkedRepositoryId = null,
        CancellationToken cancellationToken = default)
    {
        var dashboard = await _dashboardQueryService.GetDashboardAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            cancellationToken);

        // Installation/access-request details are management concerns and are
        // intentionally omitted from the shared read-only project contract.
        dashboard = dashboard with
        {
            AuthorizedInstallationId = null,
            HasUnacknowledgedAccess = false
        };

        return ApiOk(dashboard);
    }

    [HttpGet("activity")]
    public async Task<ActionResult<ApiResponse<GitHubCompatibilityPage<GitHubDashboardCommitResponse>>>> GetActivity(
        Guid projectId,
        [FromQuery] Guid? linkedRepositoryId = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 10,
        CancellationToken cancellationToken = default)
    {
        return ApiOk(await _dashboardQueryService.GetActivityAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            page,
            size,
            cancellationToken));
    }

    [HttpGet("contributors")]
    public async Task<ActionResult<ApiResponse<GitHubCompatibilityPage<GitHubDashboardContributorResponse>>>> GetContributors(
        Guid projectId,
        [FromQuery] Guid? linkedRepositoryId = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 10,
        CancellationToken cancellationToken = default)
    {
        return ApiOk(await _dashboardQueryService.GetContributorsAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            page,
            size,
            cancellationToken));
    }

    [HttpGet("repositories/{linkedRepositoryId:guid}/commits")]
    public async Task<ActionResult<ApiResponse<GitHubEvidencePage<GitHubCommitResponse>>>> GetCommits(
        Guid projectId,
        Guid linkedRepositoryId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        CancellationToken cancellationToken = default)
    {
        return ApiOk(await _evidenceQueryService.GetCommitsAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            page,
            size,
            cancellationToken));
    }

    [HttpGet("repositories/{linkedRepositoryId:guid}/contributors")]
    public async Task<ActionResult<ApiResponse<GitHubEvidencePage<GitHubContributorResponse>>>> GetRepositoryContributors(
        Guid projectId,
        Guid linkedRepositoryId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        CancellationToken cancellationToken = default)
    {
        return ApiOk(await _evidenceQueryService.GetContributorsAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            page,
            size,
            cancellationToken));
    }

    [HttpGet("repositories/{linkedRepositoryId:guid}/pull-requests")]
    public async Task<ActionResult<ApiResponse<GitHubEvidencePage<GitHubPullRequestResponse>>>> GetPullRequests(
        Guid projectId,
        Guid linkedRepositoryId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        return ApiOk(await _evidenceQueryService.GetPullRequestsAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            page,
            size,
            status,
            search,
            cancellationToken));
    }

    private Guid GetRequiredUserId()
    {
        var subject = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        if (Guid.TryParse(subject, out var userId))
        {
            return userId;
        }

        throw new ApiException(
            StatusCodes.Status401Unauthorized,
            ErrorCodes.Unauthorized,
            "Authentication is required.");
    }
}
