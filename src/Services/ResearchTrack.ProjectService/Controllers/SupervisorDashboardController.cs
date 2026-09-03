using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Features.Dashboard;

namespace ResearchTrack.ProjectService.Controllers;

[Route("api/v1/supervisor/dashboard")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class SupervisorDashboardController : ApiControllerBase
{
    private readonly ISupervisorDashboardService _dashboardService;

    public SupervisorDashboardController(
        ISupervisorDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<SupervisorDashboardResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<SupervisorDashboardResponse>>> Get(
        CancellationToken cancellationToken)
    {
        var dashboard = await _dashboardService.GetAsync(
            GetRequiredUserId(),
            cancellationToken);

        return ApiOk(dashboard);
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

    private static ApiException CreateUnauthorizedException() => new(
        StatusCodes.Status401Unauthorized,
        ErrorCodes.Unauthorized,
        "Authentication is required.");
}
