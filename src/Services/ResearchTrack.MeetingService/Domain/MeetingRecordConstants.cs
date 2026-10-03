namespace ResearchTrack.MeetingService.Domain;

public static class MeetingRecordConstants
{
    public const int DiscussionSummaryMaxLength = 1024;
    public const int DiscussionDetailsMaxLength = 5000;
    public const int UserDisplayNameMaxLength = MeetingChannelConstants.UserDisplayNameMaxLength;

    public const string StatusPending = "PENDING";
    public const string StatusApproved = "APPROVED";
}
