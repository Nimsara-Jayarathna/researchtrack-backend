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
 private readonly IJiraIssueQueryService _query;private readonly IJiraSyncService _sync;private readonly IJiraSprintProgressService _sprintProgress;private readonly IJiraWorkloadService _workload;private readonly Infrastructure.IProjectAuthorizationClient _auth;
 public ProjectJiraIssuesController(IJiraIssueQueryService query,IJiraSyncService sync,IJiraSprintProgressService sprintProgress,IJiraWorkloadService workload,Infrastructure.IProjectAuthorizationClient auth){_query=query;_sync=sync;_sprintProgress=sprintProgress;_workload=workload;_auth=auth;}
 [HttpGet("issues")]public async Task<ActionResult<ApiResponse<JiraIssueListResponse>>> Issues(Guid projectId,CancellationToken ct)=>ApiOk(await _query.GetIssuesAsync(projectId,ct));
 [HttpGet("health")]public async Task<ActionResult<ApiResponse<JiraHealthResponse>>> Health(Guid projectId,CancellationToken ct)=>ApiOk(await _query.GetHealthAsync(projectId,ct));
 [HttpGet("sprint-progress")]public async Task<ActionResult<ApiResponse<JiraSprintProgressResponse>>> SprintProgress(Guid projectId,CancellationToken ct)=>ApiOk(await _sprintProgress.GetAsync(projectId,ct));
 [HttpGet("workload")]public async Task<ActionResult<ApiResponse<JiraWorkloadResponse>>> Workload(Guid projectId,CancellationToken ct)=>ApiOk(await _workload.GetAsync(projectId,ct));
 [Authorize(Policy=ResearchTrack.BuildingBlocks.Api.Security.AuthSecurityConstants.Policies.SupervisorOnly)]
 [HttpPost("refresh")]public async Task<ActionResult<ApiResponse<JiraHealthResponse>>> Refresh(Guid projectId,CancellationToken ct){await _auth.EnsureCanManageAsync(projectId,ct);await _sync.SynchronizeAsync(projectId,ct);return ApiOk(await _query.GetHealthAsync(projectId,ct));}
}
