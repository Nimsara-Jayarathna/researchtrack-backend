using System.Security.Claims; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc; using ResearchTrack.BuildingBlocks.Api.Constants; using ResearchTrack.BuildingBlocks.Api.Contracts; using ResearchTrack.BuildingBlocks.Api.Controllers; using ResearchTrack.BuildingBlocks.Api.Exceptions; using ResearchTrack.BuildingBlocks.Api.Security; using ResearchTrack.JiraService.Contracts; using ResearchTrack.JiraService.Features;
namespace ResearchTrack.JiraService.Controllers;
[Authorize(Policy=AuthSecurityConstants.Policies.SupervisorOnly)] public sealed class JiraOAuthController:ApiControllerBase
{
 private readonly IJiraConnectionService _service; public JiraOAuthController(IJiraConnectionService service)=>_service=service;
 [HttpPost("api/v1/jira/oauth/complete")] public async Task<ActionResult<ApiResponse<JiraOAuthCompleteResponse>>> Complete([FromBody]JiraOAuthCompleteRequest request,CancellationToken ct)=>ApiOk(await _service.CompleteOAuthAsync(UserId(),request,ct));
 private Guid UserId(){var s=User.FindFirstValue(AuthSecurityConstants.SubjectClaim);if(Guid.TryParse(s,out var id))return id;throw new ApiException(401,ErrorCodes.Unauthorized,"Authentication is required.");}
}
