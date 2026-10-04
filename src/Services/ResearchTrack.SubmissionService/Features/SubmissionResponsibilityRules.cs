using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.SubmissionService.Contracts;
using ResearchTrack.SubmissionService.Domain;
using ResearchTrack.SubmissionService.Infrastructure;

namespace ResearchTrack.SubmissionService.Features;

internal static class SubmissionResponsibilityRules
{
    public static string NormalizeMode(string? raw)
    {
        var normalized = string.IsNullOrWhiteSpace(raw)
            ? SubmissionConstants.ResponsibilityMode.ProjectLeader
            : raw.Trim().ToUpperInvariant();

        if (!SubmissionConstants.ResponsibilityMode.IsKnown(normalized))
            throw new ApiValidationException([
                new ApiFieldError("responsibilityMode", ["Submission responsibility must be PROJECT_LEADER or ASSIGNED_STUDENT."])
            ]);

        return normalized;
    }

    public static (string Mode, Guid? AssignedStudentId, string? AssignedStudentName) ValidateForSave(
        string? rawMode,
        Guid? assignedStudentId,
        ProjectSubmissionContext context)
    {
        var mode = NormalizeMode(rawMode);
        if (mode == SubmissionConstants.ResponsibilityMode.ProjectLeader)
        {
            if (context.Leader is null)
                throw new ApiValidationException([
                    new ApiFieldError("responsibilityMode", ["Assign a Project Leader first, or choose a specific student for this requirement."])
                ]);
            return (mode, null, null);
        }

        if (!assignedStudentId.HasValue || assignedStudentId == Guid.Empty)
            throw new ApiValidationException([
                new ApiFieldError("assignedStudentId", ["Choose one active project student as the responsible submitter."])
            ]);

        var student = context.FindStudent(assignedStudentId.Value);
        if (student is null)
            throw new ApiValidationException([
                new ApiFieldError("assignedStudentId", ["The selected student is no longer an active member of this project."])
            ]);

        return (mode, student.Id, student.DisplayName);
    }

    public static SubmissionResponsibilityResponse BuildResponse(
        SubmissionRequirement requirement,
        ProjectSubmissionContext context)
    {
        if (requirement.ResponsibilityMode == SubmissionConstants.ResponsibilityMode.AssignedStudent)
        {
            var assigned = requirement.AssignedStudentId.HasValue
                ? context.FindStudent(requirement.AssignedStudentId.Value)
                : null;
            return new SubmissionResponsibilityResponse(
                requirement.ResponsibilityMode,
                requirement.AssignedStudentId,
                requirement.AssignedStudentName,
                assigned?.Id,
                assigned?.DisplayName ?? requirement.AssignedStudentName,
                assigned is null ? null : SubmissionConstants.SubmitterRole.AssignedStudent,
                assigned is null);
        }

        var leader = context.Leader;
        return new SubmissionResponsibilityResponse(
            SubmissionConstants.ResponsibilityMode.ProjectLeader,
            null,
            null,
            leader?.Id,
            leader?.DisplayName,
            leader is null ? null : SubmissionConstants.SubmitterRole.ProjectLeader,
            leader is null);
    }

    public static string EnsureCanSubmit(
        SubmissionRequirement requirement,
        ProjectSubmissionContext context,
        Guid userId)
    {
        if (requirement.ResponsibilityMode == SubmissionConstants.ResponsibilityMode.AssignedStudent)
        {
            if (!requirement.AssignedStudentId.HasValue)
                throw AssignmentRequired("This requirement does not have a responsible submitter. Ask the Supervisor to assign one.");

            var assigned = context.FindStudent(requirement.AssignedStudentId.Value);
            if (assigned is null)
                throw AssignmentRequired("The assigned submitter is no longer an active project member. Ask the Supervisor to choose another student.");

            if (assigned.Id != userId)
                throw new ApiException(
                    StatusCodes.Status403Forbidden,
                    ErrorCodes.Forbidden,
                    $"Only {assigned.DisplayName}, the assigned submitter, can upload an official version for this requirement.");

            return context.Leader?.Id == assigned.Id
                ? SubmissionConstants.SubmitterRole.ProjectLeader
                : SubmissionConstants.SubmitterRole.AssignedStudent;
        }

        if (context.Leader is null)
            throw AssignmentRequired("This project does not currently have a Project Leader. Ask the Supervisor to assign a leader or choose a specific submitter.");

        if (context.Leader.Id != userId)
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                $"Only {context.Leader.DisplayName}, the current Project Leader, can upload an official version for this requirement.");

        return SubmissionConstants.SubmitterRole.ProjectLeader;
    }

    private static ApiException AssignmentRequired(string message) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.Conflict,
        message);
}
