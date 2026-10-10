using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.SubmissionService.Domain;
using ResearchTrack.SubmissionService.Features;
using ResearchTrack.SubmissionService.Infrastructure;

namespace ResearchTrack.SubmissionService.MutationTests;

public sealed class SubmissionResponsibilityRulesTests
{
    private static readonly ProjectStudentContext Leader =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Leader Student", "leader@example.com", "IT0001");

    private static readonly ProjectStudentContext Assigned =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Assigned Student", "assigned@example.com", "IT0002");

    [Fact]
    public void Project_leader_is_the_default_responsibility_when_a_leader_exists()
    {
        var context = new ProjectSubmissionContext(Leader, [Leader, Assigned]);

        var result = SubmissionResponsibilityRules.ValidateForSave(null, null, context);

        Assert.Equal(SubmissionConstants.ResponsibilityMode.ProjectLeader, result.Mode);
        Assert.Null(result.AssignedStudentId);
        Assert.Null(result.AssignedStudentName);
    }

    [Fact]
    public void Project_leader_mode_requires_a_current_project_leader()
    {
        var context = new ProjectSubmissionContext(null, [Assigned]);

        Assert.Throws<ApiValidationException>(() =>
            SubmissionResponsibilityRules.ValidateForSave(
                SubmissionConstants.ResponsibilityMode.ProjectLeader,
                null,
                context));
    }

    [Fact]
    public void Assigned_student_mode_requires_an_active_project_student()
    {
        var context = new ProjectSubmissionContext(Leader, [Leader, Assigned]);

        var result = SubmissionResponsibilityRules.ValidateForSave(
            SubmissionConstants.ResponsibilityMode.AssignedStudent,
            Assigned.Id,
            context);

        Assert.Equal(SubmissionConstants.ResponsibilityMode.AssignedStudent, result.Mode);
        Assert.Equal(Assigned.Id, result.AssignedStudentId);
        Assert.Equal(Assigned.DisplayName, result.AssignedStudentName);
    }

    [Fact]
    public void Current_project_leader_can_submit_and_role_is_snapshotted_as_project_leader()
    {
        var requirement = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.ProjectLeader
        };
        var context = new ProjectSubmissionContext(Leader, [Leader, Assigned]);

        var role = SubmissionResponsibilityRules.EnsureCanSubmit(requirement, context, Leader.Id);

        Assert.Equal(SubmissionConstants.SubmitterRole.ProjectLeader, role);
    }

    [Fact]
    public void Assigned_student_can_submit_and_role_is_snapshotted_as_assigned_submitter()
    {
        var requirement = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.AssignedStudent,
            AssignedStudentId = Assigned.Id,
            AssignedStudentName = Assigned.DisplayName
        };
        var context = new ProjectSubmissionContext(Leader, [Leader, Assigned]);

        var role = SubmissionResponsibilityRules.EnsureCanSubmit(requirement, context, Assigned.Id);

        Assert.Equal(SubmissionConstants.SubmitterRole.AssignedStudent, role);
    }

    [Fact]
    public void Explicitly_assigned_current_leader_is_still_snapshotted_as_project_leader()
    {
        var requirement = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.AssignedStudent,
            AssignedStudentId = Leader.Id,
            AssignedStudentName = Leader.DisplayName
        };
        var context = new ProjectSubmissionContext(Leader, [Leader, Assigned]);

        var role = SubmissionResponsibilityRules.EnsureCanSubmit(requirement, context, Leader.Id);

        Assert.Equal(SubmissionConstants.SubmitterRole.ProjectLeader, role);
    }

    [Fact]
    public void Project_leader_responsibility_resolves_the_current_leader_without_rewriting_history()
    {
        var previousLeader = new ProjectStudentContext(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Previous Leader",
            "previous@example.com",
            "IT0003");
        var requirement = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.ProjectLeader
        };
        var currentContext = new ProjectSubmissionContext(Leader, [Leader, Assigned]);
        var historicalVersion = new SubmissionVersion
        {
            UploadedBy = previousLeader.Id,
            UploadedByName = previousLeader.DisplayName,
            SubmitterRoleSnapshot = SubmissionConstants.SubmitterRole.ProjectLeader,
            ResponsibilityModeSnapshot = SubmissionConstants.ResponsibilityMode.ProjectLeader
        };

        var responsibility = SubmissionResponsibilityRules.BuildResponse(requirement, currentContext);

        Assert.Equal(Leader.Id, responsibility.ResponsibleStudentId);
        Assert.Equal(Leader.DisplayName, responsibility.ResponsibleStudentName);
        Assert.Equal(previousLeader.Id, historicalVersion.UploadedBy);
        Assert.Equal(previousLeader.DisplayName, historicalVersion.UploadedByName);
        Assert.Equal(SubmissionConstants.SubmitterRole.ProjectLeader, historicalVersion.SubmitterRoleSnapshot);
    }
}
