using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Features;

namespace ResearchTrack.JiraService.Controllers;

[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
[Route("api/v1/jira/dashboard")]
public sealed class JiraDashboardController : ApiControllerBase
{
    private readonly IJiraDashboardHealthService _service;

    public JiraDashboardController(IJiraDashboardHealthService service)
    {
        _service = service;
    }

    [HttpPost("health")]
    public async Task<ActionResult<ApiResponse<JiraDashboardHealthResponse>>> Health(
        [FromBody] JiraDashboardHealthRequest request,
        CancellationToken cancellationToken)
    {
        return ApiOk(await _service.GetAsync(
            request.ProjectIds ?? [],
            cancellationToken));
    }
}
