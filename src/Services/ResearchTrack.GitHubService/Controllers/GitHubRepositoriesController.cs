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

[Route("api/github/repositories")]
[Route("api/v1/github/repositories")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class GitHubRepositoriesController : ApiControllerBase
{
    private readonly IRepositoryLinkService _repositoryLinkService;
    private readonly IPublicAccessSourceService _publicAccessSourceService;
    private readonly IGitHubInstallationRepositoryService _installationRepositoryService;

    public GitHubRepositoriesController(
        IRepositoryLinkService repositoryLinkService,
        IPublicAccessSourceService publicAccessSourceService,
        IGitHubInstallationRepositoryService installationRepositoryService)
    {
        _repositoryLinkService = repositoryLinkService;
        _publicAccessSourceService = publicAccessSourceService;
        _installationRepositoryService = installationRepositoryService;
    }

    [HttpGet("available")]
    [ProducesResponseType<ApiResponse<GitHubAvailableRepositoriesResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<GitHubAvailableRepositoriesResponse>>> GetAvailable(
        [FromQuery] Guid sourceId,
        CancellationToken cancellationToken)
    {
        var userId = GetRequiredUserId();
        var installationAvailable = await _installationRepositoryService.TryGetAvailableAsync(
            userId,
            sourceId,
            cancellationToken);
        if (installationAvailable is not null)
        {
            return ApiOk(installationAvailable);
        }

        var publicAvailable = await _publicAccessSourceService.GetAvailableAsync(
            userId,
            sourceId,
            cancellationToken);
        return ApiOk(publicAvailable);
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
