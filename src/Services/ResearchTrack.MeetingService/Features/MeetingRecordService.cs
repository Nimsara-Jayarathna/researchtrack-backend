using System.Globalization;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Contracts;
using ResearchTrack.MeetingService.Domain;
using ResearchTrack.MeetingService.Infrastructure;
using ResearchTrack.MeetingService.Persistence;

namespace ResearchTrack.MeetingService.Features;

public sealed class MeetingRecordService : IMeetingRecordService
{
    private const string MeetingDateFormat = "yyyy-MM-dd";

    private readonly IMeetingRecordRepository _recordRepository;
    private readonly IMeetingChannelRepository _channelRepository;
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IUserProfileClient _userProfileClient;
    private readonly TimeProvider _timeProvider;

    public MeetingRecordService(
        IMeetingRecordRepository recordRepository,
        IMeetingChannelRepository channelRepository,
        IProjectAuthorizationClient projectAuthorization,
        IUserProfileClient userProfileClient,
        TimeProvider timeProvider)
    {
        _recordRepository = recordRepository;
        _channelRepository = channelRepository;
        _projectAuthorization = projectAuthorization;
        _userProfileClient = userProfileClient;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<MeetingRecordResponse>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        var records = await _recordRepository.ListByProjectAsync(projectId, cancellationToken);
        return records.Select(ToResponse).ToArray();
    }

    public async Task<MeetingRecordResponse> CreateAsync(
        Guid projectId,
        Guid userId,
        string role,
        MeetingRecordUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedRole = NormalizeRole(role);

        if (normalizedRole == AuthSecurityConstants.Roles.Supervisor)
        {
            await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        }
        else if (normalizedRole == AuthSecurityConstants.Roles.Student)
        {
            await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        }
        else
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Your role cannot create meeting records.");
        }

        var values = await ValidateAsync(projectId, request, cancellationToken);
        var displayName =
            await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();

        var record = normalizedRole == AuthSecurityConstants.Roles.Supervisor
            ? MeetingRecord.CreateSupervisor(
                projectId,
                values.MeetingDate,
                values.DurationMinutes,
                values.DiscussionSummary,
                values.DiscussionDetails,
                values.ChannelId,
                userId,
                displayName,
                now)
            : MeetingRecord.CreateStudent(
                projectId,
                values.MeetingDate,
                values.DurationMinutes,
                values.DiscussionSummary,
                values.DiscussionDetails,
                values.ChannelId,
                userId,
                displayName,
                now);

        await _recordRepository.AddAsync(record, cancellationToken);
        return ToResponse(record);
    }

    public async Task<MeetingRecordResponse> UpdateAsync(
        Guid projectId,
        Guid recordId,
        MeetingRecordUpsertRequest request,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var record = await RequireRecordAsync(projectId, recordId, cancellationToken);
        var values = await ValidateAsync(projectId, request, cancellationToken);

        record.Update(
            values.MeetingDate,
            values.DurationMinutes,
            values.DiscussionSummary,
            values.DiscussionDetails,
            values.ChannelId,
            _timeProvider.GetUtcNow());

        await _recordRepository.SaveAsync(record, cancellationToken);
        return ToResponse(record);
    }

    public async Task DeleteAsync(
        Guid projectId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var record = await RequireRecordAsync(projectId, recordId, cancellationToken);
        await _recordRepository.DeleteAsync(record, cancellationToken);
    }

    public async Task<MeetingRecordResponse> ApproveAsync(
        Guid projectId,
        Guid recordId,
        Guid supervisorId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var record = await RequireRecordAsync(projectId, recordId, cancellationToken);

        if (!string.Equals(
                record.Status,
                MeetingRecordConstants.StatusPending,
                StringComparison.Ordinal))
        {
            throw Validation("status", "Only pending meeting records can be approved.");
        }

        var displayName =
            await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);

        record.Approve(
            supervisorId,
            displayName,
            _timeProvider.GetUtcNow());

        await _recordRepository.SaveAsync(record, cancellationToken);
        return ToResponse(record);
    }

    private async Task<MeetingRecord> RequireRecordAsync(
        Guid projectId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        return await _recordRepository.FindAsync(projectId, recordId, cancellationToken)
            ?? throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested meeting record was not found.");
    }

    private async Task<ValidatedRecordValues> ValidateAsync(
        Guid projectId,
        MeetingRecordUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApiFieldError>();
        var rawDate = (request.MeetingDate ?? string.Empty).Trim();
        DateOnly parsedMeetingDate = default;

        if (rawDate.Length == 0)
        {
            errors.Add(new ApiFieldError("meetingDate", ["Meeting date is required."]));
        }
        else if (!DateOnly.TryParseExact(
                     rawDate,
                     MeetingDateFormat,
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.None,
                     out parsedMeetingDate))
        {
            errors.Add(new ApiFieldError(
                "meetingDate",
                ["Meeting date must use YYYY-MM-DD format."]));
        }

        if (request.DurationMinutes <= 0)
        {
            errors.Add(new ApiFieldError(
                "durationMinutes",
                ["Duration must be greater than zero minutes."]));
        }

        var discussionSummary = (request.DiscussionSummary ?? string.Empty).Trim();
        if (discussionSummary.Length == 0)
        {
            errors.Add(new ApiFieldError(
                "discussionSummary",
                ["Discussion summary is required."]));
        }
        else if (discussionSummary.Length > MeetingRecordConstants.DiscussionSummaryMaxLength)
        {
            errors.Add(new ApiFieldError(
                "discussionSummary",
                [$"Discussion summary must be at most {MeetingRecordConstants.DiscussionSummaryMaxLength} characters."]));
        }

        var discussionDetails = string.IsNullOrWhiteSpace(request.DiscussionDetails)
            ? null
            : request.DiscussionDetails.Trim();

        if (discussionDetails?.Length > MeetingRecordConstants.DiscussionDetailsMaxLength)
        {
            errors.Add(new ApiFieldError(
                "discussionDetails",
                [$"Discussion details must be at most {MeetingRecordConstants.DiscussionDetailsMaxLength} characters."]));
        }

        if (request.ChannelId.HasValue)
        {
            var channel = await _channelRepository.FindAsync(
                projectId,
                request.ChannelId.Value,
                cancellationToken);

            if (channel is null)
            {
                errors.Add(new ApiFieldError(
                    "channelId",
                    ["The selected meeting channel does not belong to this project."]));
            }
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        return new ValidatedRecordValues(
            parsedMeetingDate.ToDateTime(TimeOnly.MinValue),
            request.DurationMinutes,
            discussionSummary,
            discussionDetails,
            request.ChannelId);
    }

    private static string NormalizeRole(string role) =>
        (role ?? string.Empty).Trim().ToUpperInvariant();

    private static ApiValidationException Validation(string field, string message) =>
        new([new ApiFieldError(field, [message])]);

    private static MeetingRecordResponse ToResponse(MeetingRecord record) =>
        new(
            record.Id,
            record.ProjectId,
            record.MeetingDate.ToString(MeetingDateFormat, CultureInfo.InvariantCulture),
            record.DurationMinutes,
            record.DiscussionSummary,
            record.DiscussionDetails,
            record.ChannelId,
            record.AddedBy,
            record.AddedByName,
            record.AddedByRole,
            record.Status,
            record.ApprovedBy,
            record.ApprovedByName,
            record.ApprovedAt,
            record.CreatedAt,
            record.UpdatedAt);

    private sealed record ValidatedRecordValues(
        DateTime MeetingDate,
        int DurationMinutes,
        string DiscussionSummary,
        string? DiscussionDetails,
        Guid? ChannelId);
}
