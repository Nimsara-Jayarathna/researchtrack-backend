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

[Route("api/github/access-source")]
[Route("api/v1/github/access-source")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class GitHubAccessSourcesController : ApiControllerBase
{
    private readonly IPublicAccessSourceService _accessSourceService;

    public GitHubAccessSourcesController(IPublicAccessSourceService accessSourceService)
    {
        _accessSourceService = accessSourceService;
    }

    [HttpPost("public")]
    [ProducesResponseType<ApiResponse<GitHubAvailableRepositoriesResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<GitHubAvailableRepositoriesResponse>>> CreatePublic(
        [FromBody] CreatePublicAccessSourceRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _accessSourceService.CreateAsync(
            GetRequiredUserId(),
            request,
            cancellationToken);

        return ApiCreated($"/api/github/access-source/{created.SourceId}", created);
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
