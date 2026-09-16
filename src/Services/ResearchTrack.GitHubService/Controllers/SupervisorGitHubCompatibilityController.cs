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
using ResearchTrack.GitHubService.Features.Synchronization;

namespace ResearchTrack.GitHubService.Controllers;

[Route("api/supervisor/projects/{projectId:guid}/github")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class SupervisorGitHubCompatibilityController : ApiControllerBase
{
    private readonly IGitHubInstallationRepositoryService _installationRepositoryService;
    private readonly IRepositoryLinkService _repositoryLinkService;
    private readonly IProjectGitHubInventoryService _inventoryService;
    private readonly IGitHubEvidenceQueryService _evidenceQueryService;
    private readonly IGitHubDashboardQueryService _dashboardQueryService;

    public SupervisorGitHubCompatibilityController(
        IGitHubInstallationRepositoryService installationRepositoryService,
        IRepositoryLinkService repositoryLinkService,
        IProjectGitHubInventoryService inventoryService,
        IGitHubEvidenceQueryService evidenceQueryService,
        IGitHubDashboardQueryService dashboardQueryService)
    {
        _installationRepositoryService = installationRepositoryService;
        _repositoryLinkService = repositoryLinkService;
        _inventoryService = inventoryService;
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
        return ApiOk(await _dashboardQueryService.GetDashboardAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            cancellationToken));
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
    public async Task<ActionResult<ApiResponse<GitHubCompatibilityPage<GitHubDashboardContributorResponse>>>> GetDashboardContributors(
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

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<GitHubProjectSyncQueuedResponse>>> RefreshProject(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await _repositoryLinkService.GetProjectAsync(
            GetRequiredUserId(),
            projectId,
            cancellationToken);
        foreach (var repository in project.Repositories.Where(item => item.Enabled
            && !string.Equals(item.SyncStatus, ResearchTrack.GitHubService.Domain.GitHubSyncStatuses.Pending, StringComparison.Ordinal)
            && !string.Equals(item.SyncStatus, ResearchTrack.GitHubService.Domain.GitHubSyncStatuses.InProgress, StringComparison.Ordinal)))
        {
            await _repositoryLinkService.RequestManualSyncAsync(
                GetRequiredUserId(), projectId, repository.Id, cancellationToken);
        }

        return ApiOk(new GitHubProjectSyncQueuedResponse(projectId, "QUEUED"));
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


    [HttpGet("repositories/inventory")]
    [ProducesResponseType<ApiResponse<ProjectGitHubRepositoryListingResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ProjectGitHubRepositoryListingResponse>>> GetInventory(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.GetAsync(
            GetRequiredUserId(),
            projectId,
            cancellationToken);
        return ApiOk(result);
    }

    [HttpPost("repositories/{linkedRepositoryId:guid}/sync")]
    [ProducesResponseType<ApiResponse<GitHubSyncQueuedResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GitHubSyncQueuedResponse>>> SyncRepository(
        Guid projectId,
        Guid linkedRepositoryId,
        CancellationToken cancellationToken)
    {
        await _repositoryLinkService.RequestManualSyncAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            cancellationToken);
        return ApiOk(new GitHubSyncQueuedResponse(linkedRepositoryId, "QUEUED"));
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
    public async Task<ActionResult<ApiResponse<GitHubEvidencePage<GitHubContributorResponse>>>> GetContributors(
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

    [HttpGet("repositories/{linkedRepositoryId:guid}/sync-runs")]
    public async Task<ActionResult<ApiResponse<GitHubEvidencePage<GitHubSyncRunResponse>>>> GetSyncRuns(
        Guid projectId,
        Guid linkedRepositoryId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 25,
        CancellationToken cancellationToken = default)
    {
        return ApiOk(await _evidenceQueryService.GetSyncRunsAsync(
            GetRequiredUserId(),
            projectId,
            linkedRepositoryId,
            page,
            size,
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
