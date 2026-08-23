using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Features.Users;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.AuthService.Controllers;

[Route("api/v1/users")]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserDirectoryService _userDirectoryService;

    public UsersController(IUserDirectoryService userDirectoryService)
    {
        _userDirectoryService = userDirectoryService;
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpGet("students")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<UserDirectoryResponse>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserDirectoryResponse>>>> SearchStudents(
        [FromQuery(Name = "query")] string? query,
        [FromQuery(Name = "q")] string? legacyQuery,
        CancellationToken cancellationToken)
    {
        var students = await _userDirectoryService.SearchStudentsAsync(
            query ?? legacyQuery,
            cancellationToken);
        return ApiOk(students);
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpPost("students/resolve")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<UserDirectoryResponse>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserDirectoryResponse>>>> ResolveStudents(
        [FromBody] ResolveStudentsRequest request,
        CancellationToken cancellationToken)
    {
        var students = await _userDirectoryService.ResolveStudentsAsync(
            request.StudentIds ?? [],
            cancellationToken);
        return ApiOk(students);
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.Authenticated)]
    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<UserDirectoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserDirectoryResponse>>> Me(
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        if (!Guid.TryParse(subject, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthorized,
                "Authentication is required.");
        }

        var user = await _userDirectoryService.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthorized,
                "Authentication is required.");
        }

        return ApiOk(user);
    }
}
