using System.ComponentModel.DataAnnotations;
using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Contracts;

public sealed record MeetingRecordUpsertRequest(
    [Required]
    [MaxLength(10)]
    string MeetingDate,
    [Range(1, int.MaxValue)]
    int DurationMinutes,
    [Required]
    [MaxLength(MeetingRecordConstants.DiscussionSummaryMaxLength)]
    string DiscussionSummary,
    [MaxLength(MeetingRecordConstants.DiscussionDetailsMaxLength)]
    string? DiscussionDetails,
    Guid? ChannelId);

public sealed record MeetingRecordResponse(
    Guid Id,
    Guid ProjectId,
    string MeetingDate,
    int DurationMinutes,
    string DiscussionSummary,
    string? DiscussionDetails,
    Guid? ChannelId,
    Guid AddedBy,
    string AddedByName,
    string AddedByRole,
    string Status,
    Guid? ApprovedBy,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
