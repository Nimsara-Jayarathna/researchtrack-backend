namespace ResearchTrack.MeetingService.Domain;

public static class MeetingChannelConstants
{
    public const int ChannelNameMaxLength = 120;
    public const int LinkMaxLength = 1024;
    public const int UserDisplayNameMaxLength = 201;

    public const string StatusPending = "PENDING";
    public const string StatusApproved = "APPROVED";

    public static readonly IReadOnlySet<string> SupportedPlatforms =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "GOOGLE_MEET",
            "ZOOM",
            "TEAMS",
            "WHATSAPP",
            "OTHER"
        };
}
