using ResearchTrack.JiraService.Contracts;
namespace ResearchTrack.JiraService.Features;
public interface IJiraSyncStateQueryService { Task<JiraProjectSyncStateResponse> GetAsync(Guid projectId, CancellationToken cancellationToken); }
