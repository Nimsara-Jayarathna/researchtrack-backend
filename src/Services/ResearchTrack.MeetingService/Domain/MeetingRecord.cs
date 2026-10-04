using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.MeetingService.Domain;

public sealed class MeetingRecord
{
    private MeetingRecord()
    {
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateTime MeetingDate { get; private set; }
    public int DurationMinutes { get; private set; }
    public string DiscussionSummary { get; private set; } = string.Empty;
    public string? DiscussionDetails { get; private set; }
    public Guid? ChannelId { get; private set; }
    public Guid AddedBy { get; private set; }
    public string AddedByName { get; private set; } = string.Empty;
    public string AddedByRole { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Guid? ApprovedBy { get; private set; }
    public string? ApprovedByName { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static MeetingRecord CreateSupervisor(
        Guid projectId,
        DateTime meetingDate,
        int durationMinutes,
        string discussionSummary,
        string? discussionDetails,
        Guid? channelId,
        Guid supervisorId,
        string supervisorName,
        DateTimeOffset now)
    {
        return new MeetingRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            MeetingDate = meetingDate,
            DurationMinutes = durationMinutes,
            DiscussionSummary = discussionSummary,
            DiscussionDetails = discussionDetails,
            ChannelId = channelId,
            AddedBy = supervisorId,
            AddedByName = supervisorName,
            AddedByRole = AuthSecurityConstants.Roles.Supervisor,
            Status = MeetingRecordConstants.StatusApproved,
            ApprovedBy = supervisorId,
            ApprovedByName = supervisorName,
            ApprovedAt = now,
            CreatedAt = now,
            UpdatedAt = null
        };
    }

    public static MeetingRecord CreateStudent(
        Guid projectId,
        DateTime meetingDate,
        int durationMinutes,
        string discussionSummary,
        string? discussionDetails,
        Guid? channelId,
        Guid studentId,
        string studentName,
        DateTimeOffset now)
    {
        return new MeetingRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            MeetingDate = meetingDate,
            DurationMinutes = durationMinutes,
            DiscussionSummary = discussionSummary,
            DiscussionDetails = discussionDetails,
            ChannelId = channelId,
            AddedBy = studentId,
            AddedByName = studentName,
            AddedByRole = AuthSecurityConstants.Roles.Student,
            Status = MeetingRecordConstants.StatusPending,
            ApprovedBy = null,
            ApprovedByName = null,
            ApprovedAt = null,
            CreatedAt = now,
            UpdatedAt = null
        };
    }

    public void Update(
        DateTime meetingDate,
        int durationMinutes,
        string discussionSummary,
        string? discussionDetails,
        Guid? channelId,
        DateTimeOffset now)
    {
        MeetingDate = meetingDate;
        DurationMinutes = durationMinutes;
        DiscussionSummary = discussionSummary;
        DiscussionDetails = discussionDetails;
        ChannelId = channelId;
        UpdatedAt = now;
    }

    public void Approve(Guid supervisorId, string supervisorName, DateTimeOffset now)
    {
        Status = MeetingRecordConstants.StatusApproved;
        ApprovedBy = supervisorId;
        ApprovedByName = supervisorName;
        ApprovedAt = now;
        UpdatedAt = now;
    }
}
