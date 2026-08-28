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

[Route("api/supervisor")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class SupervisorController : ApiControllerBase
{
    private readonly IProjectService _projectService;

    public SupervisorController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType<ApiResponse<SupervisorDashboardResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<SupervisorDashboardResponse>>> GetDashboard(
        CancellationToken cancellationToken)
    {
        var userId = GetRequiredUserId();
        var dashboard = await _projectService.GetSupervisorDashboardAsync(userId, cancellationToken);
        return ApiOk(dashboard);
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