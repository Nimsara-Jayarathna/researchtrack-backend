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
    private readonly IGitHubInstallationRepositoryService _installationRepositoryService;

    public GitHubRepositoriesController(IRepositoryLinkService repositoryLinkService, IGitHubInstallationRepositoryService installationRepositoryService)
    {
        _repositoryLinkService = repositoryLinkService;
        _installationRepositoryService = installationRepositoryService;
    }

    [HttpGet("available")]
    public async Task<ActionResult<ApiResponse<GitHubAvailableRepositoriesResponse>>> GetAvailable([FromQuery] Guid sourceId, CancellationToken cancellationToken)
    {
        var available = await _installationRepositoryService.TryGetAvailableAsync(GetRequiredUserId(), sourceId, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "The active GitHub App access source was not found.");
        return ApiOk(available);
    }

    [HttpPost("link")]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> Link([FromBody] LinkGitHubRepositoriesRequest request, CancellationToken cancellationToken)
        => ApiCreated(null, await _repositoryLinkService.LinkAsync(GetRequiredUserId(), request, cancellationToken));

    [HttpDelete("{linkedRepositoryId:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> Unlink(Guid linkedRepositoryId, CancellationToken cancellationToken)
        => ApiOk(await _repositoryLinkService.UnlinkAsync(GetRequiredUserId(), linkedRepositoryId, cancellationToken));

    [HttpPost("{linkedRepositoryId:guid}/enable")]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> Enable(Guid linkedRepositoryId, CancellationToken cancellationToken)
        => ApiOk(await _repositoryLinkService.SetEnabledAsync(GetRequiredUserId(), linkedRepositoryId, true, cancellationToken));

    [HttpPost("{linkedRepositoryId:guid}/disable")]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> Disable(Guid linkedRepositoryId, CancellationToken cancellationToken)
        => ApiOk(await _repositoryLinkService.SetEnabledAsync(GetRequiredUserId(), linkedRepositoryId, false, cancellationToken));

    [HttpPost("{linkedRepositoryId:guid}/select")]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> Select(Guid linkedRepositoryId, CancellationToken cancellationToken)
        => ApiOk(await _repositoryLinkService.SelectPrimaryAsync(GetRequiredUserId(), linkedRepositoryId, cancellationToken));

    [HttpPost("{linkedRepositoryId:guid}/display-name")]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoriesResponse>>> DisplayName(
        Guid linkedRepositoryId,
        [FromBody] UpdateGitHubRepositoryDisplayNameRequest request,
        CancellationToken cancellationToken)
        => ApiOk(await _repositoryLinkService.UpdateDisplayNameAsync(GetRequiredUserId(), linkedRepositoryId, request.CustomName, cancellationToken));

    private Guid GetRequiredUserId()
    {
        var subject = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        if (Guid.TryParse(subject, out var userId)) return userId;
        throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
    }
}
