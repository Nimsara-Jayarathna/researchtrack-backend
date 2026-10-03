using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Contracts;
using ResearchTrack.MeetingService.Domain;
using ResearchTrack.MeetingService.Infrastructure;
using ResearchTrack.MeetingService.Persistence;

namespace ResearchTrack.MeetingService.Features;

public sealed class MeetingChannelService : IMeetingChannelService
{
    private readonly IMeetingChannelRepository _repository;
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IUserProfileClient _userProfileClient;
    private readonly TimeProvider _timeProvider;

    public MeetingChannelService(
        IMeetingChannelRepository repository,
        IProjectAuthorizationClient projectAuthorization,
        IUserProfileClient userProfileClient,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _projectAuthorization = projectAuthorization;
        _userProfileClient = userProfileClient;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<MeetingChannelResponse>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanAccessAsync(projectId, cancellationToken);
        var channels = await _repository.ListByProjectAsync(projectId, cancellationToken);
        return channels.Select(ToResponse).ToArray();
    }

    public async Task<MeetingChannelResponse> CreateAsync(
        Guid projectId,
        Guid userId,
        string role,
        MeetingChannelUpsertRequest request,
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
                "Your role cannot create meeting channels.");
        }

        var values = Validate(request);
        var displayName =
            await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();

        var channel = normalizedRole == AuthSecurityConstants.Roles.Supervisor
            ? MeetingChannel.CreateSupervisor(
                projectId,
                values.Platform,
                values.ChannelName,
                values.Link,
                userId,
                displayName,
                now)
            : MeetingChannel.CreateStudent(
                projectId,
                values.Platform,
                values.ChannelName,
                values.Link,
                userId,
                displayName,
                now);

        await _repository.AddAsync(channel, cancellationToken);
        return ToResponse(channel);
    }

    public async Task<MeetingChannelResponse> UpdateAsync(
        Guid projectId,
        Guid channelId,
        MeetingChannelUpsertRequest request,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);

        var channel =
            await RequireChannelAsync(projectId, channelId, cancellationToken);
        var values = Validate(request);

        channel.Update(
            values.Platform,
            values.ChannelName,
            values.Link,
            _timeProvider.GetUtcNow());

        await _repository.SaveAsync(channel, cancellationToken);
        return ToResponse(channel);
    }

    public async Task DeleteAsync(
        Guid projectId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var channel =
            await RequireChannelAsync(projectId, channelId, cancellationToken);
        await _repository.DeleteAsync(channel, cancellationToken);
    }

    public async Task<MeetingChannelResponse> ApproveAsync(
        Guid projectId,
        Guid channelId,
        Guid supervisorId,
        CancellationToken cancellationToken)
    {
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var channel =
            await RequireChannelAsync(projectId, channelId, cancellationToken);

        if (!string.Equals(
                channel.Status,
                MeetingChannelConstants.StatusPending,
                StringComparison.Ordinal))
        {
            throw Validation(
                "status",
                "Only pending meeting channels can be approved.");
        }

        var displayName =
            await _userProfileClient.GetCurrentUserDisplayNameAsync(cancellationToken);

        channel.Approve(
            supervisorId,
            displayName,
            _timeProvider.GetUtcNow());

        await _repository.SaveAsync(channel, cancellationToken);
        return ToResponse(channel);
    }

    private async Task<MeetingChannel> RequireChannelAsync(
        Guid projectId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        return await _repository.FindAsync(projectId, channelId, cancellationToken)
            ?? throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested meeting channel was not found.");
    }

    private static (string Platform, string ChannelName, string Link) Validate(
        MeetingChannelUpsertRequest request)
    {
        var errors = new List<ApiFieldError>();

        var platform = (request.Platform ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        if (!MeetingChannelConstants.SupportedPlatforms.Contains(platform))
        {
            errors.Add(new ApiFieldError(
                "platform",
                ["Unsupported meeting platform."]));
        }

        var channelName = (request.ChannelName ?? string.Empty).Trim();

        if (channelName.Length == 0)
        {
            errors.Add(new ApiFieldError(
                "channelName",
                ["Channel name is required."]));
        }
        else if (channelName.Length > MeetingChannelConstants.ChannelNameMaxLength)
        {
            errors.Add(new ApiFieldError(
                "channelName",
                [$"Channel name must be at most {MeetingChannelConstants.ChannelNameMaxLength} characters."]));
        }

        var link = (request.LinkOrIdentifier ?? string.Empty).Trim();

        if (link.Length == 0)
        {
            errors.Add(new ApiFieldError(
                "linkOrIdentifier",
                ["Meeting link is required."]));
        }
        else if (link.Length > MeetingChannelConstants.LinkMaxLength)
        {
            errors.Add(new ApiFieldError(
                "linkOrIdentifier",
                [$"Meeting link must be at most {MeetingChannelConstants.LinkMaxLength} characters."]));
        }
        else if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                 || string.IsNullOrWhiteSpace(uri.Host))
        {
            errors.Add(new ApiFieldError(
                "linkOrIdentifier",
                ["Enter a valid link starting with http:// or https://."]));
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        return (platform, channelName, link);
    }

    private static string NormalizeRole(string role) =>
        (role ?? string.Empty).Trim().ToUpperInvariant();

    private static ApiValidationException Validation(
        string field,
        string message) =>
        new([new ApiFieldError(field, [message])]);

    private static MeetingChannelResponse ToResponse(MeetingChannel channel) =>
        new(
            channel.Id,
            channel.ProjectId,
            channel.Platform,
            channel.ChannelName,
            channel.LinkOrIdentifier,
            channel.AddedBy,
            channel.AddedByName,
            channel.AddedByRole,
            channel.Status,
            channel.ApprovedBy,
            channel.ApprovedByName,
            channel.ApprovedAt,
            channel.CreatedAt,
            channel.UpdatedAt);
}
