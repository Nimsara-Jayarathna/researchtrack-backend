namespace ResearchTrack.MeetingService.Infrastructure;

public interface IUserProfileClient
{
    Task<string> GetCurrentUserDisplayNameAsync(CancellationToken cancellationToken);
}
