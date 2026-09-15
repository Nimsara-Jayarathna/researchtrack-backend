namespace ResearchTrack.GitHubService.Features.Synchronization;

public enum GitHubSynchronizationOutcome
{
    Completed,
    SkippedUnavailable,
    AlreadyRunning
}
