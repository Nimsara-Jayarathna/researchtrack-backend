using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Features.Projects;

namespace ResearchTrack.ProjectService.Controllers;

[Route("api/v1/projects")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class ProjectsController : ApiControllerBase
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<ProjectResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> Create(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var supervisorUserId = GetRequiredUserId();
        var created = await _projectService.CreateAsync(supervisorUserId, request, cancellationToken);
        return ApiCreated($"/api/v1/projects/{created.Id}", created);
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ProjectSummaryResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectSummaryResponse>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var supervisorUserId = GetRequiredUserId();
        var projects = await _projectService.GetOwnedProjectsAsync(supervisorUserId, cancellationToken);
        return ApiOk(projects);
    }

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ApiResponse<ProjectResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> GetById(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var supervisorUserId = GetRequiredUserId();
        var project = await _projectService.GetOwnedProjectAsync(supervisorUserId, projectId, cancellationToken);
        if (project is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested project was not found.");
        }

        return ApiOk(project);
    }

    private Guid GetRequiredUserId()
    {
        var subject = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        if (!Guid.TryParse(subject, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthorized,
                "Authentication is required.");
        }
        return userId;
    }
}
