using ResearchTrack.SubmissionService.Contracts;

namespace ResearchTrack.SubmissionService.Features;

public interface ISubmissionRequirementService
{
    Task<IReadOnlyList<SubmissionRequirementResponse>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<SubmissionRequirementResponse> GetAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken);
    Task<SubmissionRequirementResponse> CreateAsync(Guid projectId, Guid userId, SubmissionRequirementCreateRequest request, CancellationToken cancellationToken);
    Task<SubmissionRequirementResponse> UpdateAsync(Guid projectId, Guid requirementId, SubmissionRequirementUpdateRequest request, CancellationToken cancellationToken);
    Task<SubmissionRequirementResponse> CloseAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken);
    Task<SubmissionRequirementResponse> ReopenAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken);
    Task<SubmissionRequirementResponse> ArchiveAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid projectId, Guid requirementId, CancellationToken cancellationToken);
}
