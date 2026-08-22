using Microsoft.AspNetCore.Mvc;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Contracts;
using ResearchTrack.AuthService.Features.Registration;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;

namespace ResearchTrack.AuthService.Controllers;

[Route("api/v1/auth")]
public sealed class RegistrationController : ApiControllerBase
{
    private const string AccessCookieName = "ss_access_token";
    private const string RefreshCookieName = "ss_refresh_token";

    private readonly IRegistrationService _registrationService;
    private readonly RegistrationOptions _registrationOptions;
    private readonly PasswordPolicyOptions _passwordPolicyOptions;
    private readonly JwtOptions _jwtOptions;
    private readonly AuthCookieOptions _cookieOptions;

    public RegistrationController(
        IRegistrationService registrationService,
        RegistrationOptions registrationOptions,
        PasswordPolicyOptions passwordPolicyOptions,
        JwtOptions jwtOptions,
        AuthCookieOptions cookieOptions)
    {
        _registrationService = registrationService;
        _registrationOptions = registrationOptions;
        _passwordPolicyOptions = passwordPolicyOptions;
        _jwtOptions = jwtOptions;
        _cookieOptions = cookieOptions;
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

    // SuperviseSuite: POST /api/auth/register/init
    [HttpPost("register/init")]
    [ProducesResponseType<ApiResponse<RegisterInitResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RegisterInitResponse>>> RegisterInit(
        [FromBody] RegisterInitRequest request,
        CancellationToken cancellationToken)
    {
        await _registrationService.InitRegistrationAsync(request.Email, cancellationToken);
        return ApiOk(new RegisterInitResponse("OTP sent successfully"));
    }

    // SuperviseSuite: POST /api/auth/register/verify
    [HttpPost("register/verify")]
    [ProducesResponseType<ApiResponse<RegisterVerifyResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RegisterVerifyResponse>>> RegisterVerify(
        [FromBody] RegisterVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _registrationService.VerifyOtpAsync(request.Email, request.Otp, cancellationToken);
        return ApiOk(response);
    }

    // SuperviseSuite: POST /api/auth/register/complete
    [HttpPost("register/complete")]
    [ProducesResponseType<ApiResponse<RegistrationCompleteResponse>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<RegistrationCompleteResponse>>> RegisterComplete(
        [FromBody] RegisterCompleteRequest request,
        CancellationToken cancellationToken)
    {
        var completed = await _registrationService.CompleteRegistrationAsync(request, cancellationToken);

        Response.Cookies.Append(AccessCookieName, completed.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _cookieOptions.Secure,
            SameSite = SameSiteMode.Strict,
            Path = "/api",
            MaxAge = TimeSpan.FromMinutes(_jwtOptions.AccessTokenMinutes)
        });

        Response.Cookies.Append(RefreshCookieName, completed.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _cookieOptions.Secure,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            MaxAge = TimeSpan.FromDays(_jwtOptions.RefreshTokenDays)
        });

        return ApiCreated("/api/v1/auth/register/complete", completed.Response);
    }
}
