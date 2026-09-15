using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features.Webhooks;

namespace ResearchTrack.GitHubService.Controllers;

[Route("api/github/webhooks")]
[Route("api/v1/github/webhooks")]
[AllowAnonymous]
public sealed class GitHubWebhooksController : ApiControllerBase
{
    private readonly IGitHubWebhookIngressService _ingress;

    public GitHubWebhooksController(IGitHubWebhookIngressService ingress)
    {
        _ingress = ingress;
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<ApiResponse<GitHubWebhookAcceptedResponse>>> Receive(
        CancellationToken cancellationToken)
    {
        var result = await _ingress.AcceptAsync(
            Request.Body,
            Request.ContentLength,
            Request.Headers[GitHubWebhookHeaders.Signature256].FirstOrDefault(),
            Request.Headers[GitHubWebhookHeaders.Delivery].FirstOrDefault(),
            Request.Headers[GitHubWebhookHeaders.Event].FirstOrDefault(),
            cancellationToken);

        var response = ApiResponse<GitHubWebhookAcceptedResponse>.Ok(
            new GitHubWebhookAcceptedResponse(
                result.DeliveryId,
                result.EventType,
                result.Duplicate,
                result.Status),
            new ApiMeta(HttpContext.TraceIdentifier, DateTimeOffset.UtcNow));
        return StatusCode(StatusCodes.Status202Accepted, response);
    }
}
