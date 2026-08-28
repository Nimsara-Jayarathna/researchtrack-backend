using ResearchTrack.ProjectService.Contracts;

namespace ResearchTrack.ProjectService.Features.Dashboard;

public interface ISupervisorDashboardService
{
    Task<SupervisorDashboardResponse> GetAsync(
        Guid supervisorUserId,
        CancellationToken cancellationToken);
}
