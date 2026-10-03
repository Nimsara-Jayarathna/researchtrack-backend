using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.MeetingService.Domain;

public sealed class MeetingChannel
{
    private MeetingChannel()
    {
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string ChannelName { get; private set; } = string.Empty;
    public string LinkOrIdentifier { get; private set; } = string.Empty;
    public Guid AddedBy { get; private set; }
    public string AddedByName { get; private set; } = string.Empty;
    public string AddedByRole { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Guid? ApprovedBy { get; private set; }
    public string? ApprovedByName { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static MeetingChannel CreateSupervisor(
        Guid projectId,
        string platform,
        string channelName,
        string linkOrIdentifier,
        Guid supervisorId,
        string supervisorName,
        DateTimeOffset now)
    {
        return new MeetingChannel
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Platform = platform,
            ChannelName = channelName,
            LinkOrIdentifier = linkOrIdentifier,
            AddedBy = supervisorId,
            AddedByName = supervisorName,
            AddedByRole = AuthSecurityConstants.Roles.Supervisor,
            Status = MeetingChannelConstants.StatusApproved,
            ApprovedBy = supervisorId,
            ApprovedByName = supervisorName,
            ApprovedAt = now,
            CreatedAt = now,
            UpdatedAt = null
        };
    }

    public static MeetingChannel CreateStudent(
        Guid projectId,
        string platform,
        string channelName,
        string linkOrIdentifier,
        Guid studentId,
        string studentName,
        DateTimeOffset now)
    {
        return new MeetingChannel
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Platform = platform,
            ChannelName = channelName,
            LinkOrIdentifier = linkOrIdentifier,
            AddedBy = studentId,
            AddedByName = studentName,
            AddedByRole = AuthSecurityConstants.Roles.Student,
            Status = MeetingChannelConstants.StatusPending,
            ApprovedBy = null,
            ApprovedByName = null,
            ApprovedAt = null,
            CreatedAt = now,
            UpdatedAt = null
        };
    }

    public void Update(
        string platform,
        string channelName,
        string linkOrIdentifier,
        DateTimeOffset now)
    {
        Platform = platform;
        ChannelName = channelName;
        LinkOrIdentifier = linkOrIdentifier;
        UpdatedAt = now;
    }

    public void Approve(Guid supervisorId, string supervisorName, DateTimeOffset now)
    {
        Status = MeetingChannelConstants.StatusApproved;
        ApprovedBy = supervisorId;
        ApprovedByName = supervisorName;
        ApprovedAt = now;
        UpdatedAt = now;
    }
}
