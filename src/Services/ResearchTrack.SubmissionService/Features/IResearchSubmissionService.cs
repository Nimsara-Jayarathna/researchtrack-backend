using ResearchTrack.SubmissionService.Contracts;

namespace ResearchTrack.SubmissionService.Features;

public interface IResearchSubmissionService
{
    Task<IReadOnlyList<ResearchSubmissionResponse>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ResearchSubmissionResponse> GetAsync(Guid projectId, Guid submissionId, CancellationToken cancellationToken);
    Task<SubmissionUploadSessionResponse> CreateInitialUploadSessionAsync(Guid projectId, Guid requirementId, Guid userId, CreateUploadSessionRequest request, CancellationToken cancellationToken);
    Task<ResearchSubmissionResponse> CompleteUploadSessionAsync(Guid projectId, Guid uploadSessionId, Guid userId, CancellationToken cancellationToken);
    Task<SubmissionDownloadUrlResponse> GetDownloadUrlAsync(Guid projectId, Guid submissionId, Guid versionId, bool inline, CancellationToken cancellationToken);
}
