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
    private readonly IRegistrationService _registrationService;
    private readonly RegistrationOptions _registrationOptions;
    private readonly PasswordPolicyOptions _passwordPolicyOptions;

    public RegistrationController(
        IRegistrationService registrationService,
        RegistrationOptions registrationOptions,
        PasswordPolicyOptions passwordPolicyOptions)
    {
        _registrationService = registrationService;
        _registrationOptions = registrationOptions;
        _passwordPolicyOptions = passwordPolicyOptions;
    }

    [HttpGet("register/config")]
    [ProducesResponseType<ApiResponse<RegistrationConfigResponse>>(StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<RegistrationConfigResponse>> GetRegistrationConfig()
    {
        var response = new RegistrationConfigResponse(
            true,
            $"@{_registrationOptions.StudentEmailDomain}",
            $"@{_registrationOptions.SupervisorEmailDomain}",
            true,
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
}
