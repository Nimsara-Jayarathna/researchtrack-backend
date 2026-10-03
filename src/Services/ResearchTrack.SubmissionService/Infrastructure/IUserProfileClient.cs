namespace ResearchTrack.SubmissionService.Infrastructure;

public interface IUserProfileClient
{
    Task<string> GetCurrentUserDisplayNameAsync(CancellationToken cancellationToken);
}
