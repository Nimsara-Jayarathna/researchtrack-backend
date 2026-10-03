using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Contracts;
using ResearchTrack.MeetingService.Controllers;

namespace ResearchTrack.MeetingService.Tests.Features;

public sealed class StudentMeetingChannelAuthorizationTests
{
    [Fact]
    public void Channel_controller_keeps_read_and_create_shared_but_management_supervisor_only()
    {
        var controllerPolicy = typeof(ProjectMeetingChannelsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute => attribute.Policy)
            .ToArray();

        Assert.Contains(AuthSecurityConstants.Policies.Authenticated, controllerPolicy);

        Assert.DoesNotContain(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingChannelsController.List)));
        Assert.DoesNotContain(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingChannelsController.Create)));

        Assert.Contains(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingChannelsController.Update)));
        Assert.Contains(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingChannelsController.Delete)));
        Assert.Contains(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingChannelsController.Approve)));
    }

    [Fact]
    public void Student_create_contract_cannot_supply_server_owned_approval_fields()
    {
        var constructor = Assert.Single(typeof(MeetingChannelCreateRequest).GetConstructors());
        var names = constructor
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToArray();

        Assert.Equal(
            new[] { "Platform", "ChannelName", "LinkOrIdentifier" },
            names);

        Assert.DoesNotContain("Status", names);
        Assert.DoesNotContain("ApprovedBy", names);
        Assert.DoesNotContain("ApprovedByName", names);
        Assert.DoesNotContain("ApprovedAt", names);
        Assert.DoesNotContain("AddedBy", names);
        Assert.DoesNotContain("AddedByRole", names);
    }

    private static string?[] PoliciesOn(string methodName) =>
        typeof(ProjectMeetingChannelsController)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute => attribute.Policy)
            .ToArray();
}
