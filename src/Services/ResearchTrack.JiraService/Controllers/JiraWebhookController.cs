using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.JiraService.Features;
namespace ResearchTrack.JiraService.Controllers;
[ApiController][Route("api/v1/jira/webhooks")][AllowAnonymous] public sealed class JiraWebhookController:ControllerBase
{
 private readonly IJiraWebhookService _service;public JiraWebhookController(IJiraWebhookService service)=>_service=service;
 [HttpPost][RequestSizeLimit(2_097_152)] public async Task<IActionResult> Receive(CancellationToken ct){using var reader=new StreamReader(Request.Body);var payload=await reader.ReadToEndAsync(ct);var ok=await _service.ReceiveAsync(Request.Headers.Authorization.ToString(),Request.Headers["X-Atlassian-Webhook-Identifier"].ToString(),payload,ct);return ok?Ok():Unauthorized();}
}
