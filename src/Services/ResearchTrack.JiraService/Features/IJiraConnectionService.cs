using ResearchTrack.JiraService.Contracts;
namespace ResearchTrack.JiraService.Features;
public interface IJiraConnectionService
{
 Task<JiraAuthUrlResponse> StartAsync(Guid userId,Guid projectId,CancellationToken ct);
 Task<JiraOAuthCompleteResponse> CompleteOAuthAsync(Guid userId,JiraOAuthCompleteRequest request,CancellationToken ct);
 Task<JiraBoardListResponse> GetBoardsAsync(Guid userId,Guid projectId,string selectionToken,string jiraProjectId,CancellationToken ct);
 Task<JiraConnectionResponse> LinkAsync(Guid userId,Guid projectId,LinkJiraProjectRequest request,CancellationToken ct);
 Task<JiraConnectionResponse?> GetConnectionAsync(Guid projectId,CancellationToken ct);
 Task DisconnectAsync(Guid userId,Guid projectId,CancellationToken ct);
 Task StartAuthorizationCheckOnly(Guid projectId,CancellationToken ct);
}
