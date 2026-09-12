using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Features.Passwords;
using ResearchTrack.AuthService.Infrastructure.Cookies;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;

namespace ResearchTrack.AuthService.Controllers;

[AllowAnonymous]
[Route("api/v1/auth")]
public sealed class PasswordResetController : ApiControllerBase
{
    private readonly IPasswordResetService _passwordResetService;
    private readonly IAuthCookieService _cookieService;

    public PasswordResetController(
        IPasswordResetService passwordResetService,
        IAuthCookieService cookieService)
    {
        _passwordResetService = passwordResetService;
        _cookieService = cookieService;
    }

    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _passwordResetService.RequestResetAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpGet("reset-password/validate")]
    [ProducesResponseType<ApiResponse<ValidateResetTokenResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ValidateResetTokenResponse>>> ValidateResetToken(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var response = await _passwordResetService.ValidateTokenAsync(token, cancellationToken);
        return ApiOk(response);
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _passwordResetService.ResetPasswordAsync(request, cancellationToken);
        _cookieService.ClearSession(Response);
        return NoContent();
    }
}
