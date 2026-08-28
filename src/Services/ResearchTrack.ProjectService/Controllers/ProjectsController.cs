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
[Authorize(Policy = AuthSecurityConstants.Policies.Authenticated)]
public sealed class ProjectsController : ApiControllerBase
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost]
    [ProducesResponseType<ApiResponse<CreateProjectResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<CreateProjectResponse>>> Create(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetRequiredUserId();
        var created = await _projectService.CreateAsync(userId, request, cancellationToken);
        return ApiCreated($"/api/v1/projects/{created.Id}", created);
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ProjectSummaryResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectSummaryResponse>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var projects = await _projectService.GetAccessibleProjectsAsync(
            GetRequiredUserId(),
            GetRequiredRole(),
            cancellationToken);
        return ApiOk(projects);
    }

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ApiResponse<ProjectResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> GetById(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await _projectService.GetAccessibleProjectAsync(
            GetRequiredUserId(),
            GetRequiredRole(),
            projectId,
            cancellationToken);

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
            throw CreateUnauthorizedException();
        }
        return userId;
    }

    private string GetRequiredRole()
    {
        var role = User.FindFirstValue(AuthSecurityConstants.RoleClaim);
        if (string.IsNullOrWhiteSpace(role))
        {
            throw CreateUnauthorizedException();
        }
        return role.Trim().ToUpperInvariant();
    }

    private static ApiException CreateUnauthorizedException() => new(
        StatusCodes.Status401Unauthorized,
        ErrorCodes.Unauthorized,
        "Authentication is required.");
}