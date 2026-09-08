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

[Route("api/github/repositories")]
[Route("api/v1/github/repositories")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class GitHubRepositoriesController : ApiControllerBase
{
    private readonly IRepositoryLinkService _repositoryLinkService;
    private readonly IPublicAccessSourceService _accessSourceService;

    public GitHubRepositoriesController(
        IRepositoryLinkService repositoryLinkService,
        IPublicAccessSourceService accessSourceService)
    {
        _repositoryLinkService = repositoryLinkService;
        _accessSourceService = accessSourceService;
    }

    [HttpGet("available")]
    [ProducesResponseType<ApiResponse<GitHubAvailableRepositoriesResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<GitHubAvailableRepositoriesResponse>>> GetAvailable(
        [FromQuery] Guid sourceId,
        CancellationToken cancellationToken)
    {
        var available = await _accessSourceService.GetAvailableAsync(
            GetRequiredUserId(),
            sourceId,
            cancellationToken);
        return ApiOk(available);
    }

    [HttpPost("link")]
    [ProducesResponseType<ApiResponse<ProjectGitHubRepositoriesResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> Link(
        [FromBody] LinkGitHubRepositoriesRequest request,
        CancellationToken cancellationToken)
    {
        var linked = await _repositoryLinkService.LinkAsync(
            GetRequiredUserId(),
            request,
            cancellationToken);
        return ApiCreated(null, linked);
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
