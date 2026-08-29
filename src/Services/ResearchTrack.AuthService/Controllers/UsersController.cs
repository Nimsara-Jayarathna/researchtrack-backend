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
    private readonly IUserAccountService _userAccountService;

    public UsersController(
        IUserDirectoryService userDirectoryService,
        IUserAccountService userAccountService)
    {
        _userDirectoryService = userDirectoryService;
        _userAccountService = userAccountService;
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
        var user = await _userDirectoryService.GetUserAsync(
            GetRequiredUserId(),
            cancellationToken);
        if (user is null)
        {
            throw CreateUnauthorizedException();
        }

        return ApiOk(user);
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.Authenticated)]
    [HttpPatch("me/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _userAccountService.ChangePasswordAsync(
            GetRequiredUserId(),
            request,
            cancellationToken);
        return NoContent();
    }

    private Guid GetRequiredUserId()
    {
        var subject = User.FindFirstValue(AuthSecurityConstants.SubjectClaim);
        return Guid.TryParse(subject, out var userId)
            ? userId
            : throw CreateUnauthorizedException();
    }

    private static ApiException CreateUnauthorizedException() => new(
        StatusCodes.Status401Unauthorized,
        ErrorCodes.Unauthorized,
        "Authentication is required.");
}
