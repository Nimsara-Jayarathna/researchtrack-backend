using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Features.Registration;
using ResearchTrack.AuthService.Infrastructure.Cookies;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;

namespace ResearchTrack.AuthService.Controllers;

[AllowAnonymous]
[Route("api/v1/auth")]
public sealed class RegistrationController : ApiControllerBase
{
    private readonly IRegistrationService _registrationService;
    private readonly RegistrationOptions _registrationOptions;
    private readonly PasswordPolicyOptions _passwordPolicyOptions;
    private readonly IAuthCookieService _cookieService;

    public RegistrationController(
        IRegistrationService registrationService,
        RegistrationOptions registrationOptions,
        PasswordPolicyOptions passwordPolicyOptions,
        IAuthCookieService cookieService)
    {
        _registrationService = registrationService;
        _registrationOptions = registrationOptions;
        _passwordPolicyOptions = passwordPolicyOptions;
        _cookieService = cookieService;
    }

    [HttpGet("register/config")]
    [ProducesResponseType<ApiResponse<RegistrationConfigResponse>>(StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<RegistrationConfigResponse>> GetRegistrationConfig()
    {
        var response = new RegistrationConfigResponse(
            _registrationOptions.DomainRestrictionEnabled,
            $"@{_registrationOptions.StudentEmailDomain}",
            $"@{_registrationOptions.SupervisorEmailDomain}",
            _registrationOptions.EffectiveStudentEmailPrefixRestrictionEnabled,
            _registrationOptions.StudentIdentifierPattern,
            _registrationOptions.RequireStudentRegistrationNumber,
            _registrationOptions.RequireStudentRegistrationNumberToMatchEmail,
            new PasswordPolicyResponse(
                _passwordPolicyOptions.MinimumLength,
                _passwordPolicyOptions.MaximumLength,
                _passwordPolicyOptions.RequireUppercase,
                _passwordPolicyOptions.RequireLowercase,
                _passwordPolicyOptions.RequireDigit,
                _passwordPolicyOptions.RequireSpecialCharacter));

        return ApiOk(response);
    }

    // Existing ResearchTrack route: intentionally retained.
    [HttpPost("register")]
    [ProducesResponseType<ApiResponse<RegisterResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RegisterResponse>>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var registered = await _registrationService.RegisterAsync(request, cancellationToken);
        return ApiCreated($"/api/v1/users/{registered.Id}", registered);
    }

    [HttpPost("register/init")]
    [ProducesResponseType<ApiResponse<RegisterInitResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RegisterInitResponse>>> RegisterInit(
        [FromBody] RegisterInitRequest request,
        CancellationToken cancellationToken)
    {
        await _registrationService.InitRegistrationAsync(request.Email, cancellationToken);
        return ApiOk(new RegisterInitResponse("OTP sent successfully"));
    }

    [HttpPost("register/verify")]
    [ProducesResponseType<ApiResponse<RegisterVerifyResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RegisterVerifyResponse>>> RegisterVerify(
        [FromBody] RegisterVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _registrationService.VerifyOtpAsync(request.Email, request.Otp, cancellationToken);
        return ApiOk(response);
    }

    [HttpPost("register/complete")]
    [ProducesResponseType<ApiResponse<RegistrationCompleteResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<RegistrationCompleteResponse>>> RegisterComplete(
        [FromBody] RegisterCompleteRequest request,
        CancellationToken cancellationToken)
    {
        var completed = await _registrationService.CompleteRegistrationAsync(request, cancellationToken);

        _cookieService.WriteSession(Response, completed.AccessToken, completed.RefreshToken);

        return ApiCreated("/api/v1/auth/register/complete", completed.Response);
    }
}
