using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.ProjectService.Contracts;

namespace ResearchTrack.ProjectService.Features.Projects;

internal static class ProjectRequestValidator
{
    private const int TitleMaxLength = 40;
    private const int SummaryMaxLength = 250;
    private const int BatchMaxLength = 32;
    private const int SemesterMaxLength = 32;
    private const int MilestoneTitleMaxLength = 40;
    private const int MilestoneDescriptionMaxLength = 250;

    public static NormalizedCreateProject Validate(
        CreateProjectRequest request,
        DateOnly today)
    {
        var errors = new List<ApiFieldError>();
        var title = NormalizeRequired(request.Title, "title", "Title", TitleMaxLength, errors);
        var summary = NormalizeRequired(request.Summary, "summary", "Summary", SummaryMaxLength, errors);
        var batch = NormalizeRequired(request.Batch, "batch", "Batch", BatchMaxLength, errors);
        var semester = NormalizeRequired(request.Semester, "semester", "Semester", SemesterMaxLength, errors);

        var studentIds = (request.StudentIds ?? []).ToArray();
        if (studentIds.Length == 0)
        {
            errors.Add(new ApiFieldError("studentIds", ["At least one student must be selected."]));
        }
        else if (studentIds.Distinct().Count() != studentIds.Length)
        {
            errors.Add(new ApiFieldError("studentIds", ["Duplicate students are not allowed."]));
        }

        if (request.LeaderStudentId is Guid leaderStudentId
            && !studentIds.Contains(leaderStudentId))
        {
            errors.Add(new ApiFieldError(
                "leaderStudentId",
                ["Leader must be one of the selected students."]));
        }

        var milestoneRequests = request.Milestones ?? [];
        var milestones = new List<NormalizedMilestone>();
        if (milestoneRequests.Count == 0)
        {
            errors.Add(new ApiFieldError("milestones", ["At least one milestone is required."]));
        }
        else
        {
            DateOnly? previousDueDate = null;
            for (var index = 0; index < milestoneRequests.Count; index++)
            {
                var milestone = milestoneRequests[index];
                var prefix = $"milestones[{index}]";
                var milestoneTitle = NormalizeRequired(
                    milestone.Title,
                    $"{prefix}.title",
                    "Milestone title",
                    MilestoneTitleMaxLength,
                    errors);
                var description = milestone.Description?.Trim();
                if (description?.Length > MilestoneDescriptionMaxLength)
                {
                    errors.Add(new ApiFieldError(
                        $"{prefix}.description",
                        [$"Milestone description must not exceed {MilestoneDescriptionMaxLength} characters."]));
                }

                if (milestone.DueDate is null)
                {
                    errors.Add(new ApiFieldError(
                        $"{prefix}.dueDate",
                        ["Milestone due date is required."]));
                    continue;
                }

                var dueDate = milestone.DueDate.Value;
                if (dueDate < today)
                {
                    errors.Add(new ApiFieldError(
                        $"{prefix}.dueDate",
                        ["Milestone due date cannot be in the past."]));
                }

                if (previousDueDate is not null && dueDate < previousDueDate.Value)
                {
                    errors.Add(new ApiFieldError(
                        $"{prefix}.dueDate",
                        ["Milestone due dates must be in chronological order."]));
                }

                previousDueDate = dueDate;
                if (!string.IsNullOrWhiteSpace(milestoneTitle))
                {
                    milestones.Add(new NormalizedMilestone(
                        milestoneTitle,
                        string.IsNullOrWhiteSpace(description) ? null : description,
                        dueDate));
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        return new NormalizedCreateProject(
            title!,
            summary!,
            batch!,
            semester!,
            studentIds,
            request.LeaderStudentId,
            milestones);
    }

    public static IReadOnlyList<Guid> ValidateMemberStudentIds(
        AddProjectMembersRequest request)
    {
        var errors = new List<ApiFieldError>();
        var studentIds = (request.StudentIds ?? []).ToArray();

        if (studentIds.Length == 0)
        {
            errors.Add(new ApiFieldError(
                "studentIds",
                ["At least one student must be selected."]));
        }
        else if (studentIds.Distinct().Count() != studentIds.Length)
        {
            errors.Add(new ApiFieldError(
                "studentIds",
                ["Duplicate students are not allowed."]));
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        return studentIds;
    }

    public static void ValidateResolvedStudents(
        IReadOnlyCollection<Guid> requestedStudentIds,
        IReadOnlyCollection<Guid> resolvedStudentIds)
    {
        var resolved = resolvedStudentIds.ToHashSet();
        var missing = requestedStudentIds
            .Where(id => !resolved.Contains(id))
            .Distinct()
            .ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        throw new ApiValidationException([
            new ApiFieldError(
                "studentIds",
                ["One or more selected students are invalid or are no longer registered as students."])
        ]);
    }

    private static string? NormalizeRequired(
        string? value,
        string field,
        string displayName,
        int maxLength,
        ICollection<ApiFieldError> errors)
    {
        var normalized = value?.Trim();
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            messages.Add($"{displayName} is required.");
        }
        else if (normalized.Length > maxLength)
        {
            messages.Add($"{displayName} must not exceed {maxLength} characters.");
        }

        if (messages.Count > 0)
        {
            errors.Add(new ApiFieldError(field, messages));
        }

        return normalized;
    }

    internal sealed record NormalizedCreateProject(
        string Title,
        string Summary,
        string Batch,
        string Semester,
        IReadOnlyList<Guid> StudentIds,
        Guid? LeaderStudentId,
        IReadOnlyList<NormalizedMilestone> Milestones);

    internal sealed record NormalizedMilestone(
        string Title,
        string? Description,
        DateOnly DueDate);
}
