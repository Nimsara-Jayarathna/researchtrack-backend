using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
namespace ResearchTrack.JiraService.Infrastructure;
public sealed class ProjectAuthorizationClient : IProjectAuthorizationClient
{
    private readonly HttpClient _httpClient; private readonly IHttpContextAccessor _context;
    public ProjectAuthorizationClient(HttpClient httpClient, IHttpContextAccessor context) { _httpClient=httpClient; _context=context; }
    public async Task EnsureCanManageAsync(Guid projectId, CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"api/v1/projects/{projectId}"); ForwardAuthentication(request);
        HttpResponseMessage response; try { response=await _httpClient.SendAsync(request,ct); } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException) { throw new ApiException(503,ErrorCodes.DependencyUnavailable,"Project Service is unavailable.",innerException:ex); }
        using(response) { if(response.IsSuccessStatusCode) return; if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized) throw new ApiException(401,ErrorCodes.Unauthorized,"Authentication is required."); if(response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound) throw new ApiException(403,ErrorCodes.Forbidden,"Only the owning Supervisor can manage this project's Jira integration."); throw new ApiException(503,ErrorCodes.DependencyUnavailable,"Unable to verify project authorization with Project Service."); }
    }
    private void ForwardAuthentication(HttpRequestMessage request) { var incoming=_context.HttpContext?.Request ?? throw new ApiException(503,ErrorCodes.DependencyUnavailable,"Request authentication context is unavailable."); if(incoming.Headers.Authorization.Count>0) request.Headers.TryAddWithoutValidation("Authorization",incoming.Headers.Authorization.ToArray()); if(incoming.Cookies.TryGetValue(AuthSecurityConstants.AccessCookieName,out var token)&&!string.IsNullOrWhiteSpace(token)) request.Headers.TryAddWithoutValidation("Cookie",$"{AuthSecurityConstants.AccessCookieName}={token}"); }
}
