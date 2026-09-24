using System.Security.Claims; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc; using ResearchTrack.BuildingBlocks.Api.Constants; using ResearchTrack.BuildingBlocks.Api.Contracts; using ResearchTrack.BuildingBlocks.Api.Controllers; using ResearchTrack.BuildingBlocks.Api.Exceptions; using ResearchTrack.BuildingBlocks.Api.Security; using ResearchTrack.JiraService.Contracts; using ResearchTrack.JiraService.Features;
namespace ResearchTrack.JiraService.Controllers;
[Route("api/v1/projects/{projectId:guid}/jira")][Authorize(Policy=AuthSecurityConstants.Policies.SupervisorOnly)] public sealed class ProjectJiraConnectionController:ApiControllerBase
{
 private readonly IJiraConnectionService _service;public ProjectJiraConnectionController(IJiraConnectionService service)=>_service=service;
 [HttpGet("auth-url")] public async Task<ActionResult<ApiResponse<JiraAuthUrlResponse>>> AuthUrl(Guid projectId,CancellationToken ct)=>ApiOk(await _service.StartAsync(UserId(),projectId,ct));
 [HttpGet("boards")] public async Task<ActionResult<ApiResponse<JiraBoardListResponse>>> Boards(Guid projectId,[FromQuery]string selectionToken,[FromQuery]string jiraProjectId,CancellationToken ct)=>ApiOk(await _service.GetBoardsAsync(UserId(),projectId,selectionToken,jiraProjectId,ct));
 [HttpPost("link")] public async Task<ActionResult<ApiResponse<JiraConnectionResponse>>> Link(Guid projectId,[FromBody]LinkJiraProjectRequest request,CancellationToken ct)=>ApiOk(await _service.LinkAsync(UserId(),projectId,request,ct));
 [HttpPost("disconnect")] public async Task<ActionResult<ApiResponse<object>>> Disconnect(Guid projectId,CancellationToken ct){await _service.DisconnectAsync(UserId(),projectId,ct);return ApiOk<object>(new { disconnected=true });}
 [HttpGet("connection")] public async Task<ActionResult<ApiResponse<JiraConnectionResponse?>>> Connection(Guid projectId,CancellationToken ct){await _service.StartAuthorizationCheckOnly(projectId,ct);return ApiOk(await _service.GetConnectionAsync(projectId,ct));}
 private Guid UserId(){var s=User.FindFirstValue(AuthSecurityConstants.SubjectClaim);if(Guid.TryParse(s,out var id))return id;throw new ApiException(401,ErrorCodes.Unauthorized,"Authentication is required.");}
}
