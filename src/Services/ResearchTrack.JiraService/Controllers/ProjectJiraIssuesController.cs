using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Features;
namespace ResearchTrack.JiraService.Controllers;
[Authorize]
[Route("api/v1/projects/{projectId:guid}/jira")]
public sealed class ProjectJiraIssuesController:ApiControllerBase
{
 private readonly IJiraIssueQueryService _query;private readonly IJiraSyncService _sync;private readonly Infrastructure.IProjectAuthorizationClient _auth;
 public ProjectJiraIssuesController(IJiraIssueQueryService query,IJiraSyncService sync,Infrastructure.IProjectAuthorizationClient auth){_query=query;_sync=sync;_auth=auth;}
 [HttpGet("issues")]public async Task<ActionResult<ApiResponse<JiraIssueListResponse>>> Issues(Guid projectId,CancellationToken ct)=>ApiOk(await _query.GetIssuesAsync(projectId,ct));
 [HttpGet("health")]public async Task<ActionResult<ApiResponse<JiraHealthResponse>>> Health(Guid projectId,CancellationToken ct)=>ApiOk(await _query.GetHealthAsync(projectId,ct));
 [Authorize(Policy=ResearchTrack.BuildingBlocks.Api.Security.AuthSecurityConstants.Policies.SupervisorOnly)]
 [HttpPost("refresh")]public async Task<ActionResult<ApiResponse<JiraHealthResponse>>> Refresh(Guid projectId,CancellationToken ct){await _auth.EnsureCanManageAsync(projectId,ct);await _sync.SynchronizeAsync(projectId,ct);return ApiOk(await _query.GetHealthAsync(projectId,ct));}
}
