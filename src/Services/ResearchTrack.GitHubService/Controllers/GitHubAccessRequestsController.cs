using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features.Installation;

namespace ResearchTrack.GitHubService.Controllers;

[ApiController]
public sealed class GitHubAccessRequestsController : ApiControllerBase
{
    private readonly IGitHubAccessRequestService _requests;
    private readonly IGitHubInstallationFlowService _installationFlow;

    public GitHubAccessRequestsController(IGitHubAccessRequestService requests, IGitHubInstallationFlowService installationFlow)
    {
        _requests = requests;
        _installationFlow = installationFlow;
    }

    [HttpGet("api/github/access-requests/validate")]
    [HttpGet("api/v1/github/access-requests/validate")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<GitHubAccessRequestValidationResponse>>> Validate(
        [FromQuery] string token,
        CancellationToken cancellationToken) => ApiOk(await _requests.ValidateAsync(token, cancellationToken));

    [HttpPost("api/github/access-requests/continue")]
    [HttpPost("api/v1/github/access-requests/continue")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<GitHubAccessRequestContinueResponse>>> Continue(
        [FromQuery] string token,
        CancellationToken cancellationToken)
    {
        var started = await _installationFlow.StartAsync(
            Guid.Empty,
            new StartGitHubInstallationRequest(null, token),
            cancellationToken);
        return ApiOk(new GitHubAccessRequestContinueResponse(started.ProjectId, started.GitHubAuthorizeUrl));
    }

    [HttpGet("api/github/access-updated/summary")]
    [HttpGet("api/v1/github/access-updated/summary")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<GitHubAccessUpdatedSummaryResponse>>> Result(
        [FromQuery] string token,
        CancellationToken cancellationToken) => ApiOk(await _requests.GetResultAsync(token, cancellationToken));

    [HttpPost("api/github/access-updated/acknowledge")]
    [HttpPost("api/v1/github/access-updated/acknowledge")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<GitHubAccessUpdatedAcknowledgeResponse>>> Acknowledge(
        [FromQuery] string token,
        CancellationToken cancellationToken) => ApiOk(await _requests.AcknowledgeAsync(token, cancellationToken));

    [HttpGet("api/supervisor/projects/{projectId:guid}/github/access-updated/summary")]
    [HttpGet("api/v1/projects/{projectId:guid}/github/access-updated/summary")]
    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    public async Task<ActionResult<ApiResponse<GitHubAccessUpdatedSummaryResponse>>> ProjectSummary(
        Guid projectId,
        CancellationToken cancellationToken) => ApiOk(await _requests.GetLatestCompletedAsync(GetRequiredUserId(), projectId, cancellationToken));

    [HttpPost("api/supervisor/projects/{projectId:guid}/github/access-updated/acknowledge")]
    [HttpPost("api/v1/projects/{projectId:guid}/github/access-updated/acknowledge")]
    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    public async Task<ActionResult<ApiResponse<GitHubAccessUpdatedAcknowledgeResponse>>> AcknowledgeProject(
        Guid projectId,
        CancellationToken cancellationToken) => ApiOk(await _requests.AcknowledgeLatestAsync(GetRequiredUserId(), projectId, cancellationToken));


    private Guid GetRequiredUserId()
    {
        var subject = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        if (Guid.TryParse(subject, out var userId)) return userId;
        throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
    }
}
