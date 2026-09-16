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
using ResearchTrack.GitHubService.Features.Installation;

namespace ResearchTrack.GitHubService.Controllers;

[Route("api/github/access-source")]
[Route("api/v1/github/access-source")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class GitHubAccessSourcesController : ApiControllerBase
{
    private readonly IRepositoryLinkService _repositoryLinks;
    private readonly IGitHubAccessRequestService _accessRequests;

    public GitHubAccessSourcesController(IRepositoryLinkService repositoryLinks, IGitHubAccessRequestService accessRequests)
    {
        _repositoryLinks = repositoryLinks;
        _accessRequests = accessRequests;
    }

    [HttpPost("request")]
    public async Task<ActionResult<ApiResponse<GitHubAccessRequestCreateResponse>>> CreateRequest(
        [FromBody] CreateGitHubAccessRequestRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _accessRequests.CreateAsync(GetRequiredUserId(), request.ProjectId, request.OwnerLogin, cancellationToken);
        return ApiCreated(null, created);
    }

    [HttpGet("requests")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<GitHubAccessRequestSummaryResponse>>>> ListRequests(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken)
    {
        return ApiOk(await _accessRequests.ListAsync(GetRequiredUserId(), projectId, cancellationToken));
    }

    [HttpDelete("requests/{requestId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> RevokeRequest(
        Guid requestId,
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken)
    {
        await _accessRequests.RevokeAsync(GetRequiredUserId(), projectId, requestId, cancellationToken);
        return ApiOk<object>(new { projectId, requestId, status = "REVOKED" });
    }

    [HttpDelete("{sourceId:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> Disconnect(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        return ApiOk(await _repositoryLinks.DisconnectSourceAsync(GetRequiredUserId(), sourceId, cancellationToken));
    }

    private Guid GetRequiredUserId()
    {
        var subject = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        if (Guid.TryParse(subject, out var userId)) return userId;
        throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
    }
}
