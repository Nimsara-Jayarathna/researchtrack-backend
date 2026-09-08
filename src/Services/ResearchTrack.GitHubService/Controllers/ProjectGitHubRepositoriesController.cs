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

[Route("api/projects/{projectId:guid}/github-repositories")]
[Route("api/v1/projects/{projectId:guid}/github-repositories")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class ProjectGitHubRepositoriesController : ApiControllerBase
{
    private readonly IRepositoryLinkService _repositoryLinkService;

    public ProjectGitHubRepositoriesController(IRepositoryLinkService repositoryLinkService)
    {
        _repositoryLinkService = repositoryLinkService;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<ProjectGitHubRepositoriesResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var repositories = await _repositoryLinkService.GetProjectAsync(
            GetRequiredUserId(),
            projectId,
            cancellationToken);
        return ApiOk(repositories);
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
