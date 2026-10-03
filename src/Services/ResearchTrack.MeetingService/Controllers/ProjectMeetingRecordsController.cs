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
[Route("api/v1/projects/{projectId:guid}/meetings/records")]
public sealed class ProjectMeetingRecordsController : ApiControllerBase
{
    private readonly IMeetingRecordService _meetingRecordService;

    public ProjectMeetingRecordsController(IMeetingRecordService meetingRecordService)
    {
        _meetingRecordService = meetingRecordService;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MeetingRecordResponse>>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MeetingRecordResponse>>>> List(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        return ApiOk(
            await _meetingRecordService.ListAsync(projectId, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<MeetingRecordResponse>>(
        StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<MeetingRecordResponse>>> Create(
        Guid projectId,
        [FromBody] MeetingRecordUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _meetingRecordService.CreateAsync(
            projectId,
            GetRequiredUserId(),
            GetRequiredRole(),
            request,
            cancellationToken);

        return ApiCreated(
            $"/api/v1/projects/{projectId}/meetings/records/{created.Id}",
            created);
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPatch("{recordId:guid}")]
    public async Task<ActionResult<ApiResponse<MeetingRecordResponse>>> Update(
        Guid projectId,
        Guid recordId,
        [FromBody] MeetingRecordUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return ApiOk(
            await _meetingRecordService.UpdateAsync(
                projectId,
                recordId,
                request,
                cancellationToken));
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpDelete("{recordId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        await _meetingRecordService.DeleteAsync(projectId, recordId, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost("{recordId:guid}/approve")]
    public async Task<ActionResult<ApiResponse<MeetingRecordResponse>>> Approve(
        Guid projectId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        return ApiOk(
            await _meetingRecordService.ApproveAsync(
                projectId,
                recordId,
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
