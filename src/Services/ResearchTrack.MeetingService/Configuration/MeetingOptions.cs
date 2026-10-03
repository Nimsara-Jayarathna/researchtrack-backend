namespace ResearchTrack.MeetingService.Configuration;

public sealed class MeetingOptions
{
    public const string SectionName = "Meeting";

    public int DependencyTimeoutSeconds { get; init; } = 10;
}
