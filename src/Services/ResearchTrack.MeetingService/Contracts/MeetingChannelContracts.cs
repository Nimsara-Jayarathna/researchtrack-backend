using System.ComponentModel.DataAnnotations;
using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Contracts;

public sealed record MeetingChannelUpsertRequest(
    [Required]
    [MaxLength(32)]
    string Platform,
    [Required]
    [MaxLength(MeetingChannelConstants.ChannelNameMaxLength)]
    string ChannelName,
    [Required]
    [MaxLength(MeetingChannelConstants.LinkMaxLength)]
    string LinkOrIdentifier);

public sealed record MeetingChannelResponse(
    Guid Id,
    Guid ProjectId,
    string Platform,
    string ChannelName,
    string LinkOrIdentifier,
    Guid AddedBy,
    string AddedByName,
    string AddedByRole,
    string Status,
    Guid? ApprovedBy,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
