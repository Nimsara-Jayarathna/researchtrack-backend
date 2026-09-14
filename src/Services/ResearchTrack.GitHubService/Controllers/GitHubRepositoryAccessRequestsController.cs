using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features.Installation;

namespace ResearchTrack.GitHubService.Controllers;

[Route("api/github/access-requests")]
[Route("api/v1/github/access-requests")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GitHubRepositoryAccessRequestsController : ApiControllerBase
{
    private readonly IGitHubRepositoryAccessTokenService _tokenService;
    private readonly IGitHubRepositoryAccessRequestService _requestService;
    private readonly IGitHubRepositoryAccessContinuationService _continuationService;

    public GitHubRepositoryAccessRequestsController(
        IGitHubRepositoryAccessTokenService tokenService,
        IGitHubRepositoryAccessRequestService requestService,
        IGitHubRepositoryAccessContinuationService continuationService)
    {
        _tokenService = tokenService;
        _requestService = requestService;
        _continuationService = continuationService;
    }

    [HttpPost]
    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [ProducesResponseType<ApiResponse<GitHubRepositoryAccessRequestCreateResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<GitHubRepositoryAccessRequestCreateResponse>>> Create(
        [FromBody] CreateGitHubRepositoryAccessRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _requestService.CreateAsync(
            GetRequiredUserId(),
            request,
            cancellationToken);
        return ApiCreated(null, response);
    }

    [HttpGet("{requestId:guid}")]
    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [ProducesResponseType<ApiResponse<GitHubRepositoryAccessRequestMemberStatusResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<GitHubRepositoryAccessRequestMemberStatusResponse>>> GetStatus(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var response = await _requestService.GetStatusAsync(
            GetRequiredUserId(),
            requestId,
            cancellationToken);
        return ApiOk(response);
    }

    [HttpPost("continue")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<GitHubRepositoryAccessRequestContinueResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<ActionResult<ApiResponse<GitHubRepositoryAccessRequestContinueResponse>>> Continue(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var response = await _continuationService.ContinueAsync(token, cancellationToken);
        return ApiOk(response);
    }

    [HttpGet("validate")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<GitHubRepositoryAccessRequestStatusResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<GitHubRepositoryAccessRequestStatusResponse>>> Validate(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var response = await _tokenService.ValidateAsync(token, cancellationToken);
        return ApiOk(response);
    }

    private Guid GetRequiredUserId()
    {
        var subject = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        if (Guid.TryParse(subject, out var userId))
        {
            return userId;
        }

        throw new ApiException(
            StatusCodes.Status401Unauthorized,
            ErrorCodes.Unauthorized,
            "Authentication is required.");
    }
}
