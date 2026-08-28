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

    // ============================================================
    // CREATE PROJECT
    // POST: /api/v1/projects
    // ============================================================

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost]
    [ProducesResponseType<ApiResponse<CreateProjectResponse>>(
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<CreateProjectResponse>>> Create(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetRequiredUserId();

        var created = await _projectService.CreateAsync(
            userId,
            request,
            cancellationToken);

        return ApiCreated(
            $"/api/v1/projects/{created.Id}",
            created);
    }

    // ============================================================
    // GET ALL ACCESSIBLE PROJECTS
    // GET: /api/v1/projects
    // ============================================================

    [HttpGet]
    [ProducesResponseType<
        ApiResponse<IReadOnlyList<ProjectSummaryResponse>>>
        (StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<
        ActionResult<ApiResponse<IReadOnlyList<ProjectSummaryResponse>>>>
        GetAll(
            CancellationToken cancellationToken)
    {
        var projects =
            await _projectService.GetAccessibleProjectsAsync(
                GetRequiredUserId(),
                GetRequiredRole(),
                cancellationToken);

        return ApiOk(projects);
    }

    // ============================================================
    // GET PROJECT BY ID
    // GET: /api/v1/projects/{projectId}
    // ============================================================

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ApiResponse<ProjectResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> GetById(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project =
            await _projectService.GetAccessibleProjectAsync(
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

    // ============================================================
    // UPDATE PROJECT METADATA
    // PUT: /api/v1/projects/{projectId}
    //
    // Only the Supervisor who owns the project can update it.
    // ============================================================

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPut("{projectId:guid}")]
    [ProducesResponseType<ApiResponse<ProjectResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> Update(
        Guid projectId,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var supervisorUserId = GetRequiredUserId();

        var updated = await _projectService.UpdateAsync(
            supervisorUserId,
            projectId,
            request,
            cancellationToken);

        return ApiOk(updated);
    }

    // ============================================================
    // ADD PROJECT MEMBERS
    // POST: /api/v1/projects/{projectId}/members
    //
    // Only the Supervisor who owns the project can add Students.
    // ============================================================

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost("{projectId:guid}/members")]
    [ProducesResponseType<ApiResponse<ProjectResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> AddMembers(
        Guid projectId,
        [FromBody] AddProjectMembersRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _projectService.AddMembersAsync(
            GetRequiredUserId(),
            projectId,
            request,
            cancellationToken);

        return ApiOk(updated);
    }

    // ============================================================
    // REMOVE PROJECT STUDENT
    // DELETE: /api/v1/projects/{projectId}/members/{studentId}
    //
    // Only the Supervisor who owns the project can remove Students.
    // ============================================================

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpDelete("{projectId:guid}/members/{studentId:guid}")]
    [ProducesResponseType<ApiResponse<ProjectResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProjectResponse>>> RemoveStudent(
        Guid projectId,
        Guid studentId,
        CancellationToken cancellationToken)
    {
        var updated = await _projectService.RemoveStudentAsync(
            GetRequiredUserId(),
            projectId,
            studentId,
            cancellationToken);

        return ApiOk(updated);
    }

    // ============================================================
    // ADD MILESTONE
    // POST: /api/v1/projects/{projectId}/milestones
    //
    // Only the Supervisor who owns the project can add a milestone.
    // ============================================================

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost("{projectId:guid}/milestones")]
    [ProducesResponseType<ApiResponse<ProjectMilestoneResponse>>(
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<
        ActionResult<ApiResponse<ProjectMilestoneResponse>>>
        AddMilestone(
            Guid projectId,
            [FromBody] CreateProjectMilestoneRequest request,
            CancellationToken cancellationToken)
    {
        var supervisorUserId = GetRequiredUserId();

        var created =
            await _projectService.AddMilestoneAsync(
                supervisorUserId,
                projectId,
                request,
                cancellationToken);

        return ApiCreated(
            $"/api/v1/projects/{projectId}/milestones/{created.Id}",
            created);
    }

    // ============================================================
    // UPDATE MILESTONE
    // PUT: /api/v1/projects/{projectId}/milestones/{milestoneId}
    //
    // Only the Supervisor who owns the project can update it.
    // ============================================================

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPut("{projectId:guid}/milestones/{milestoneId:guid}")]
    [ProducesResponseType<ApiResponse<ProjectMilestoneResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<
        ActionResult<ApiResponse<ProjectMilestoneResponse>>>
        UpdateMilestone(
            Guid projectId,
            Guid milestoneId,
            [FromBody] UpdateProjectMilestoneRequest request,
            CancellationToken cancellationToken)
    {
        var supervisorUserId = GetRequiredUserId();

        var updated =
            await _projectService.UpdateMilestoneAsync(
                supervisorUserId,
                projectId,
                milestoneId,
                request,
                cancellationToken);

        return ApiOk(updated);
    }

    // ============================================================
    // GET CURRENT USER ID FROM JWT
    // ============================================================

    private Guid GetRequiredUserId()
    {
        var subject = User.FindFirstValue(
            AuthSecurityConstants.SubjectClaim);

        if (!Guid.TryParse(subject, out var userId))
        {
            throw CreateUnauthorizedException();
        }

        return userId;
    }

    // ============================================================
    // GET CURRENT USER ROLE FROM JWT
    // ============================================================

    private string GetRequiredRole()
    {
        var role = User.FindFirstValue(
            AuthSecurityConstants.RoleClaim);

        if (string.IsNullOrWhiteSpace(role))
        {
            throw CreateUnauthorizedException();
        }

        return role.Trim().ToUpperInvariant();
    }

    // ============================================================
    // UNAUTHORIZED EXCEPTION
    // ============================================================

    private static ApiException CreateUnauthorizedException() => new(
        StatusCodes.Status401Unauthorized,
        ErrorCodes.Unauthorized,
        "Authentication is required.");
}
