using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Features.Authentication;
using ResearchTrack.AuthService.Infrastructure.Cookies;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.AuthService.Controllers;

[Route("api/v1/auth")]
public sealed class AuthenticationController : ApiControllerBase
{
    private readonly IUserAuthenticationService _authenticationService;
    private readonly IAuthCookieService _cookieService;

    public AuthenticationController(
        IUserAuthenticationService authenticationService,
        IAuthCookieService cookieService)
    {
        _authenticationService = authenticationService;
        _cookieService = cookieService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authenticationService.LoginAsync(request, cancellationToken);
        _cookieService.WriteSession(Response, result.AccessToken, result.RefreshToken);
        return ApiOk(result.Response);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Refresh(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _authenticationService.RefreshAsync(
                _cookieService.ReadRefreshToken(Request) ?? string.Empty,
                cancellationToken);
            _cookieService.WriteSession(Response, result.AccessToken, result.RefreshToken);
            return ApiOk(result.Response);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status401Unauthorized)
        {
            _cookieService.ClearSession(Response);
            throw;
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        try
        {
            await _authenticationService.RevokeRefreshTokenAsync(
                _cookieService.ReadRefreshToken(Request),
                cancellationToken);
        }
        finally
        {
            _cookieService.ClearSession(Response);
        }

        return NoContent();
    }

    [Authorize(Policy = AuthSecurityConstants.Policies.Authenticated)]
    [HttpGet("me")]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Me(
        CancellationToken cancellationToken)
    {
        var response = await _authenticationService.GetCurrentUserAsync(User, cancellationToken);
        return ApiOk(response);
    }
}
