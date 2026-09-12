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

[Route("api/supervisor/projects/{projectId:guid}/github")]
[Route("api/v1/projects/{projectId:guid}/github")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class SupervisorGitHubCompatibilityController : ApiControllerBase
{
    private readonly IGitHubInstallationRepositoryService _installationRepositoryService;
    private readonly IRepositoryLinkService _repositoryLinkService;

    public SupervisorGitHubCompatibilityController(
        IGitHubInstallationRepositoryService installationRepositoryService,
        IRepositoryLinkService repositoryLinkService)
    {
        _installationRepositoryService = installationRepositoryService;
        _repositoryLinkService = repositoryLinkService;
    }

    [HttpGet("installations/{installationId:long}/repositories")]
    [ProducesResponseType<ApiResponse<GitHubInstallationRepositoriesPageResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<GitHubInstallationRepositoriesPageResponse>>> GetRepositories(
        Guid projectId,
        long installationId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 30,
        CancellationToken cancellationToken = default)
    {
        var response = await _installationRepositoryService.GetInstallationPageAsync(
            GetRequiredUserId(),
            projectId,
            installationId,
            page,
            size,
            cancellationToken);
        return ApiOk(response);
    }

    [HttpPost("link")]
    [ProducesResponseType<ApiResponse<ProjectGitHubRepositoryLinkResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoryLinkResponse>>> Link(
        Guid projectId,
        [FromBody] LinkProjectGitHubRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetRequiredUserId();
        var selection = await _installationRepositoryService.ResolveLegacySelectionAsync(
            userId,
            projectId,
            request.InstallationId,
            request.RepositoryId,
            cancellationToken);

        var linked = await _repositoryLinkService.LinkAsync(
            userId,
            new LinkGitHubRepositoriesRequest(
                projectId,
                selection.SourceId,
                [new LinkGitHubRepositoryRequestItem(selection.RepositoryId, null, true)]),
            cancellationToken);

        var repository = linked.Repositories.Single(item =>
            item.SourceId == selection.SourceId
            && item.GitHubRepoId == selection.GitHubRepositoryId);

        return ApiCreated(
            null,
            new ProjectGitHubRepositoryLinkResponse(
                projectId,
                request.InstallationId,
                repository.GitHubRepoId,
                repository.Name ?? string.Empty,
                repository.FullName ?? string.Empty,
                repository.Url ?? string.Empty,
                repository.OwnerLogin ?? string.Empty,
                repository.DefaultBranch,
                repository.LastSyncedAt));
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
