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
[Route("api/v1/projects/{projectId:guid}/submissions/requirements")]
public sealed class SubmissionRequirementsController : ApiControllerBase
{
    private readonly ISubmissionRequirementService _service;

    public SubmissionRequirementsController(ISubmissionRequirementService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SubmissionRequirementResponse>>>> List(Guid projectId, CancellationToken cancellationToken) =>
        ApiOk(await _service.ListAsync(projectId, cancellationToken));

    [HttpGet("{requirementId:guid}")]
    public async Task<ActionResult<ApiResponse<SubmissionRequirementResponse>>> Get(Guid projectId, Guid requirementId, CancellationToken cancellationToken) =>
        ApiOk(await _service.GetAsync(projectId, requirementId, cancellationToken));

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<SubmissionRequirementResponse>>> Create(Guid projectId, [FromBody] SubmissionRequirementCreateRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(projectId, GetRequiredUserId(), request, cancellationToken);
        return ApiCreated($"/api/v1/projects/{projectId}/submissions/requirements/{created.Id}", created);
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPatch("{requirementId:guid}")]
    public async Task<ActionResult<ApiResponse<SubmissionRequirementResponse>>> Update(Guid projectId, Guid requirementId, [FromBody] SubmissionRequirementUpdateRequest request, CancellationToken cancellationToken) =>
        ApiOk(await _service.UpdateAsync(projectId, requirementId, request, cancellationToken));

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost("{requirementId:guid}/close")]
    public async Task<ActionResult<ApiResponse<SubmissionRequirementResponse>>> Close(Guid projectId, Guid requirementId, CancellationToken cancellationToken) => ApiOk(await _service.CloseAsync(projectId, requirementId, cancellationToken));

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost("{requirementId:guid}/reopen")]
    public async Task<ActionResult<ApiResponse<SubmissionRequirementResponse>>> Reopen(Guid projectId, Guid requirementId, CancellationToken cancellationToken) => ApiOk(await _service.ReopenAsync(projectId, requirementId, cancellationToken));

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost("{requirementId:guid}/archive")]
    public async Task<ActionResult<ApiResponse<SubmissionRequirementResponse>>> Archive(Guid projectId, Guid requirementId, CancellationToken cancellationToken) => ApiOk(await _service.ArchiveAsync(projectId, requirementId, cancellationToken));

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpDelete("{requirementId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid projectId, Guid requirementId, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(projectId, requirementId, cancellationToken);
        return NoContent();
    }

    private Guid GetRequiredUserId()
    {
        var value = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
    }
}
