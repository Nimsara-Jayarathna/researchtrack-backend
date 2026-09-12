using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Controllers;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features.Installation;

namespace ResearchTrack.GitHubService.Controllers;

[Route("api/github/access-source/install")]
[Route("api/v1/github/access-source/install")]
[Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
public sealed class GitHubInstallationController : ApiControllerBase
{
    private readonly IGitHubInstallationFlowService _installationFlowService;
    private readonly GitHubAppOptions _gitHubAppOptions;

    public GitHubInstallationController(
        IGitHubInstallationFlowService installationFlowService,
        GitHubAppOptions gitHubAppOptions)
    {
        _installationFlowService = installationFlowService;
        _gitHubAppOptions = gitHubAppOptions;
    }

    [HttpPost("start")]
    [ProducesResponseType<ApiResponse<GitHubInstallStartResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<GitHubInstallStartResponse>>> Start(
        [FromBody] StartGitHubInstallationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _installationFlowService.StartAsync(
            GetRequiredUserId(),
            request,
            cancellationToken);
        return ApiOk(response);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? state,
        [FromQuery(Name = "installation_id")] long? installationId,
        [FromQuery(Name = "setup_action")] string? setupAction,
        [FromQuery] string? code,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        var result = await _installationFlowService.CompleteCallbackAsync(
            state,
            installationId,
            setupAction,
            code,
            error,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.ExternalRedirectUrl))
        {
            return Redirect(result.ExternalRedirectUrl);
        }

        return Redirect(BuildFrontendReturnUrl(result));
    }

    private string BuildFrontendReturnUrl(GitHubInstallationCallbackResult result)
    {
        var returnUri = new Uri(
            _gitHubAppOptions.FrontendReturnOrigin,
            result.ReturnPath.TrimStart('/'));
        var query = new Dictionary<string, string>
        {
            ["githubSetup"] = result.Succeeded ? "success" : "failed",
            ["githubFlow"] = result.FlowType
        };

        if (result.SourceId is Guid sourceId)
        {
            query["githubSourceId"] = sourceId.ToString("D");
        }

        if (result.InstallationId is long installationId)
        {
            query["installationId"] = installationId.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            query["githubError"] = result.ErrorCode;
        }

        var builder = new UriBuilder(returnUri)
        {
            Query = string.Join("&", query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
        };
        return builder.Uri.AbsoluteUri;
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
