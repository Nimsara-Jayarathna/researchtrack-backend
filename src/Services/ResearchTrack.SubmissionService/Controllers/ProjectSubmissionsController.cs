using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.SubmissionService.Contracts;
using ResearchTrack.SubmissionService.Features;

namespace ResearchTrack.SubmissionService.Controllers;

[Authorize(Policy = AuthSecurityConstants.Policies.Authenticated)]
[Route("api/v1/projects/{projectId:guid}/submissions")]
public sealed class ProjectSubmissionsController : ApiControllerBase
{
    private readonly IResearchSubmissionService _service;

    public ProjectSubmissionsController(IResearchSubmissionService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ResearchSubmissionResponse>>>> List(Guid projectId, CancellationToken cancellationToken) =>
        ApiOk(await _service.ListAsync(projectId, cancellationToken));

    [HttpGet("{submissionId:guid}")]
    public async Task<ActionResult<ApiResponse<ResearchSubmissionResponse>>> Get(Guid projectId, Guid submissionId, CancellationToken cancellationToken) =>
        ApiOk(await _service.GetAsync(projectId, submissionId, cancellationToken));

    [Authorize(Policy = AuthSecurityConstants.Policies.StudentOnly)]
    [HttpPost("requirements/{requirementId:guid}/upload-sessions")]
    public async Task<ActionResult<ApiResponse<SubmissionUploadSessionResponse>>> CreateUploadSession(Guid projectId, Guid requirementId, [FromBody] CreateUploadSessionRequest request, CancellationToken cancellationToken)
    {
        var session = await _service.CreateInitialUploadSessionAsync(projectId, requirementId, GetRequiredUserId(), request, cancellationToken);
        return ApiCreated($"/api/v1/projects/{projectId}/submissions/upload-sessions/{session.UploadSessionId}", session);
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.StudentOnly)]
    [HttpPost("upload-sessions/{uploadSessionId:guid}/complete")]
    public async Task<ActionResult<ApiResponse<ResearchSubmissionResponse>>> CompleteUploadSession(Guid projectId, Guid uploadSessionId, CancellationToken cancellationToken) =>
        ApiOk(await _service.CompleteUploadSessionAsync(projectId, uploadSessionId, GetRequiredUserId(), cancellationToken));

    [HttpGet("{submissionId:guid}/versions/{versionId:guid}/download-url")]
    public async Task<ActionResult<ApiResponse<SubmissionDownloadUrlResponse>>> GetDownloadUrl(Guid projectId, Guid submissionId, Guid versionId, [FromQuery] string? disposition, CancellationToken cancellationToken)
    {
        var inline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase);
        return ApiOk(await _service.GetDownloadUrlAsync(projectId, submissionId, versionId, inline, cancellationToken));
    }

    private Guid GetRequiredUserId()
    {
        var value = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
    }
}
