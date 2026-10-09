using Microsoft.AspNetCore.Http;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.SubmissionService.Domain;
using ResearchTrack.SubmissionService.Features;
using ResearchTrack.SubmissionService.Infrastructure;

namespace ResearchTrack.SubmissionService.Tests.Unit;

public sealed class SubmissionResponsibilityRulesBoundaryTests
{
    private static readonly ProjectStudentContext Leader =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Leader Student", "leader@example.com", "IT0001");

    private static readonly ProjectStudentContext Assigned =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Assigned Student", "assigned@example.com", "IT0002");

    [Theory]
    [InlineData(null, SubmissionConstants.ResponsibilityMode.ProjectLeader)]
    [InlineData("", SubmissionConstants.ResponsibilityMode.ProjectLeader)]
    [InlineData("  project_leader  ", SubmissionConstants.ResponsibilityMode.ProjectLeader)]
    [InlineData(" assigned_student ", SubmissionConstants.ResponsibilityMode.AssignedStudent)]
    public void NormalizeMode_DefaultsAndNormalizesKnownValues(string? raw, string expected)
    {
        Assert.Equal(expected, SubmissionResponsibilityRules.NormalizeMode(raw));
    }

    [Theory]
    [InlineData("student")]
    [InlineData("leader")]
    [InlineData("UNKNOWN")]
    public void NormalizeMode_RejectsUnknownValue(string raw)
    {
        var exception = Assert.Throws<ApiValidationException>(() =>
            SubmissionResponsibilityRules.NormalizeMode(raw));

        Assert.Equal("responsibilityMode", Assert.Single(exception.FieldErrors).Field);
    }

    [Theory]
    [InlineData(null)]
    public void ValidateForSave_AssignedStudentRequiresIdentifier(Guid? studentId)
    {
        var context = new ProjectSubmissionContext(Leader, [Leader, Assigned]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            SubmissionResponsibilityRules.ValidateForSave(
                SubmissionConstants.ResponsibilityMode.AssignedStudent,
                studentId,
                context));

        Assert.Equal("assignedStudentId", Assert.Single(exception.FieldErrors).Field);
    }

    [Fact]
    public void ValidateForSave_AssignedStudentRejectsEmptyAndInactiveStudentIds()
    {
        var context = new ProjectSubmissionContext(Leader, [Leader, Assigned]);

        Assert.Throws<ApiValidationException>(() =>
            SubmissionResponsibilityRules.ValidateForSave(
                SubmissionConstants.ResponsibilityMode.AssignedStudent,
                Guid.Empty,
                context));

        Assert.Throws<ApiValidationException>(() =>
            SubmissionResponsibilityRules.ValidateForSave(
                SubmissionConstants.ResponsibilityMode.AssignedStudent,
                Guid.NewGuid(),
                context));
    }

    [Fact]
    public void BuildResponse_AssignedStudentReportsStaleAssignmentWhenStudentLeftProject()
    {
        var requirement = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.AssignedStudent,
            AssignedStudentId = Assigned.Id,
            AssignedStudentName = "Historical Name"
        };
        var context = new ProjectSubmissionContext(Leader, [Leader]);

        var response = SubmissionResponsibilityRules.BuildResponse(requirement, context);

        Assert.Equal(Assigned.Id, response.AssignedStudentId);
        Assert.Equal("Historical Name", response.ResponsibleStudentName);
        Assert.True(response.RequiresAssignment);
        Assert.Null(response.ResponsibleStudentRole);
    }

    [Fact]
    public void BuildResponse_ProjectLeaderReportsReassignmentWhenProjectHasNoLeader()
    {
        var requirement = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.ProjectLeader
        };
        var context = new ProjectSubmissionContext(null, [Assigned]);

        var response = SubmissionResponsibilityRules.BuildResponse(requirement, context);

        Assert.True(response.RequiresAssignment);
        Assert.Null(response.ResponsibleStudentId);
        Assert.Null(response.ResponsibleStudentRole);
    }

    [Fact]
    public void EnsureCanSubmit_RejectsWrongAssignedStudentWithForbidden()
    {
        var requirement = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.AssignedStudent,
            AssignedStudentId = Assigned.Id,
            AssignedStudentName = Assigned.DisplayName
        };
        var context = new ProjectSubmissionContext(Leader, [Leader, Assigned]);

        var exception = Assert.Throws<ApiException>(() =>
            SubmissionResponsibilityRules.EnsureCanSubmit(requirement, context, Leader.Id));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
    }

    [Fact]
    public void EnsureCanSubmit_RejectsMissingOrInactiveAssignedStudentWithConflict()
    {
        var missingAssignment = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.AssignedStudent
        };
        var inactiveAssignment = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.AssignedStudent,
            AssignedStudentId = Assigned.Id,
            AssignedStudentName = Assigned.DisplayName
        };
        var context = new ProjectSubmissionContext(Leader, [Leader]);

        var missing = Assert.Throws<ApiException>(() =>
            SubmissionResponsibilityRules.EnsureCanSubmit(missingAssignment, context, Leader.Id));
        var inactive = Assert.Throws<ApiException>(() =>
            SubmissionResponsibilityRules.EnsureCanSubmit(inactiveAssignment, context, Leader.Id));

        Assert.Equal(StatusCodes.Status409Conflict, missing.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, inactive.StatusCode);
    }

    [Fact]
    public void EnsureCanSubmit_RejectsProjectLeaderModeWithoutLeaderOrForWrongUser()
    {
        var requirement = new SubmissionRequirement
        {
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.ProjectLeader
        };

        var noLeader = Assert.Throws<ApiException>(() =>
            SubmissionResponsibilityRules.EnsureCanSubmit(
                requirement,
                new ProjectSubmissionContext(null, [Assigned]),
                Assigned.Id));

        var wrongUser = Assert.Throws<ApiException>(() =>
            SubmissionResponsibilityRules.EnsureCanSubmit(
                requirement,
                new ProjectSubmissionContext(Leader, [Leader, Assigned]),
                Assigned.Id));

        Assert.Equal(StatusCodes.Status409Conflict, noLeader.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, wrongUser.StatusCode);
    }
}
