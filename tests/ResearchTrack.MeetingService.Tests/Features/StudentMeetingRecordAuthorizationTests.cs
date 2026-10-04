using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Contracts;
using ResearchTrack.MeetingService.Controllers;

namespace ResearchTrack.MeetingService.Tests.Features;

public sealed class StudentMeetingRecordAuthorizationTests
{
    [Fact]
    public void Record_controller_keeps_read_and_create_shared_but_management_supervisor_only()
    {
        var controllerPolicy = typeof(ProjectMeetingRecordsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute => attribute.Policy)
            .ToArray();

        Assert.Contains(AuthSecurityConstants.Policies.Authenticated, controllerPolicy);

        Assert.DoesNotContain(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingRecordsController.List)));
        Assert.DoesNotContain(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingRecordsController.Create)));

        Assert.Contains(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingRecordsController.Update)));
        Assert.Contains(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingRecordsController.Delete)));
        Assert.Contains(
            AuthSecurityConstants.Policies.SupervisorOnly,
            PoliciesOn(nameof(ProjectMeetingRecordsController.Approve)));
    }

    [Fact]
    public void Student_create_contract_contains_only_user_editable_meeting_fields()
    {
        var constructor = Assert.Single(typeof(MeetingRecordUpsertRequest).GetConstructors());
        var names = constructor
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "MeetingDate",
                "DurationMinutes",
                "DiscussionSummary",
                "DiscussionDetails",
                "ChannelId"
            },
            names);

        Assert.DoesNotContain("Status", names);
        Assert.DoesNotContain("ApprovedBy", names);
        Assert.DoesNotContain("ApprovedByName", names);
        Assert.DoesNotContain("ApprovedAt", names);
        Assert.DoesNotContain("AddedBy", names);
        Assert.DoesNotContain("AddedByName", names);
        Assert.DoesNotContain("AddedByRole", names);
        Assert.DoesNotContain("CreatedAt", names);
        Assert.DoesNotContain("UpdatedAt", names);
    }

    private static string?[] PoliciesOn(string methodName) =>
        typeof(ProjectMeetingRecordsController)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute => attribute.Policy)
            .ToArray();
}
