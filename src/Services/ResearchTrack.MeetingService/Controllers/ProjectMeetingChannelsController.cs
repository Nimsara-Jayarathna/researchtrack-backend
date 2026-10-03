using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Contracts;
using ResearchTrack.MeetingService.Features;

namespace ResearchTrack.MeetingService.Controllers;

[Authorize(Policy = AuthSecurityConstants.Policies.Authenticated)]
[Route("api/v1/projects/{projectId:guid}/meetings/channels")]
public sealed class ProjectMeetingChannelsController : ApiControllerBase
{
    private readonly IMeetingChannelService _meetingChannelService;

    public ProjectMeetingChannelsController(
        IMeetingChannelService meetingChannelService)
    {
        _meetingChannelService = meetingChannelService;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MeetingChannelResponse>>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MeetingChannelResponse>>>> List(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        return ApiOk(
            await _meetingChannelService.ListAsync(
                projectId,
                cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<MeetingChannelResponse>>(
        StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<MeetingChannelResponse>>> Create(
        Guid projectId,
        [FromBody] MeetingChannelUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _meetingChannelService.CreateAsync(
            projectId,
            GetRequiredUserId(),
            GetRequiredRole(),
            request,
            cancellationToken);

        return ApiCreated(
            $"/api/v1/projects/{projectId}/meetings/channels/{created.Id}",
            created);
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPatch("{channelId:guid}")]
    public async Task<ActionResult<ApiResponse<MeetingChannelResponse>>> Update(
        Guid projectId,
        Guid channelId,
        [FromBody] MeetingChannelUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return ApiOk(
            await _meetingChannelService.UpdateAsync(
                projectId,
                channelId,
                request,
                cancellationToken));
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpDelete("{channelId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        await _meetingChannelService.DeleteAsync(
            projectId,
            channelId,
            cancellationToken);

        return NoContent();
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost("{channelId:guid}/approve")]
    public async Task<ActionResult<ApiResponse<MeetingChannelResponse>>> Approve(
        Guid projectId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        return ApiOk(
            await _meetingChannelService.ApproveAsync(
                projectId,
                channelId,
                GetRequiredUserId(),
                cancellationToken));
    }

    private Guid GetRequiredUserId()
    {
        var value = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);

        return Guid.TryParse(value, out var userId)
            ? userId
            : throw AuthenticationRequired();
    }

    private string GetRequiredRole()
    {
        var role = User.FindFirstValue(AuthSecurityConstants.RoleClaim);

        return string.IsNullOrWhiteSpace(role)
            ? throw AuthenticationRequired()
            : role;
    }

    private static ApiException AuthenticationRequired() =>
        new(
            StatusCodes.Status401Unauthorized,
            ErrorCodes.Unauthorized,
            "Authentication is required.");
}
