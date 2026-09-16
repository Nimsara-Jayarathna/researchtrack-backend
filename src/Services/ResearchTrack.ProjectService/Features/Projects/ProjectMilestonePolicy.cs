using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.ProjectService.Domain;

namespace ResearchTrack.ProjectService.Features.Projects;

internal static class ProjectMilestonePolicy
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.Ordinal)
        {
            ProjectMilestoneStatuses.Planned,
            ProjectMilestoneStatuses.InProgress,
            ProjectMilestoneStatuses.Completed,
            ProjectMilestoneStatuses.Missed,
            ProjectMilestoneStatuses.Cancelled
        };

    private static readonly HashSet<string> OpenStatuses =
        new(StringComparer.Ordinal)
        {
            ProjectMilestoneStatuses.Planned,
            ProjectMilestoneStatuses.InProgress
        };

    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.Ordinal)
        {
            ProjectMilestoneStatuses.Completed,
            ProjectMilestoneStatuses.Missed,
            ProjectMilestoneStatuses.Cancelled
        };

    public static string NormalizeAndValidateStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            ThrowValidation(
                "status",
                "Milestone status is required.");
        }

        var normalized = rawStatus!.Trim().ToUpperInvariant();

        if (!AllowedStatuses.Contains(normalized))
        {
            ThrowValidation(
                "status",
                "Milestone status is invalid.");
        }

        return normalized;
    }

    public static void ValidateDueDateForStatus(
        DateOnly dueDate,
        string status,
        DateOnly today)
    {
        if (OpenStatuses.Contains(status) && dueDate < today)
        {
            ThrowValidation(
                "dueDate",
                "Open milestones must use today or a future due date.");
        }
    }

    public static void ValidateStatusTransition(
        string currentStatus,
        string nextStatus)
    {
        if (string.Equals(
                currentStatus,
                nextStatus,
                StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(
                currentStatus,
                ProjectMilestoneStatuses.Completed,
                StringComparison.Ordinal))
        {
            ThrowValidation(
                "status",
                "Completed milestones cannot be moved to another status.");
        }

        if (TerminalStatuses.Contains(currentStatus) &&
            OpenStatuses.Contains(nextStatus))
        {
            ThrowValidation(
                "status",
                "Terminal milestones cannot move back to open states.");
        }
    }

    public static void ValidateChronologyWithPrevious(
        DateOnly? previousDueDate,
        DateOnly currentDueDate)
    {
        if (previousDueDate is not null &&
            currentDueDate < previousDueDate.Value)
        {
            ThrowValidation(
                "dueDate",
                "Milestone due date must be on or after the previous milestone due date.");
        }
    }

    public static void ValidateChronologyForUpdate(
        IReadOnlyList<ProjectMilestone> milestones,
        Guid targetMilestoneId,
        DateOnly newDueDate)
    {
        var ordered = milestones
            .OrderBy(milestone => milestone.SequenceNo)
            .ToArray();

        var index = Array.FindIndex(
            ordered,
            milestone => milestone.Id == targetMilestoneId);

        if (index < 0)
        {
            return;
        }

        if (index > 0 &&
            newDueDate < ordered[index - 1].DueDate)
        {
            ThrowValidation(
                "dueDate",
                "Milestone due date must be on or after the previous milestone due date.");
        }

        if (index < ordered.Length - 1 &&
            newDueDate > ordered[index + 1].DueDate)
        {
            ThrowValidation(
                "dueDate",
                "Milestone due date must be on or before the next milestone due date.");
        }
    }

    public static int CalculateProgressPercent(
        IReadOnlyCollection<ProjectMilestone> milestones)
    {
        var activeMilestones = milestones
            .Where(milestone =>
                !string.Equals(
                    milestone.Status,
                    ProjectMilestoneStatuses.Cancelled,
                    StringComparison.Ordinal))
            .ToArray();

        if (activeMilestones.Length == 0)
        {
            return 0;
        }

        var completedCount = activeMilestones.Count(milestone =>
            string.Equals(
                milestone.Status,
                ProjectMilestoneStatuses.Completed,
                StringComparison.Ordinal));

        return (int)Math.Round(
            completedCount * 100.0 / activeMilestones.Length,
            MidpointRounding.AwayFromZero);
    }

    public static DateOnly? ComputeProjectMilestoneDate(
        IReadOnlyCollection<ProjectMilestone> milestones)
    {
        var openDueDates = milestones
            .Where(milestone => OpenStatuses.Contains(milestone.Status))
            .Select(milestone => milestone.DueDate)
            .ToArray();

        return openDueDates.Length == 0
            ? null
            : openDueDates.Min();
    }

    public static void ApplyAggregates(
        Project project,
        IReadOnlyCollection<ProjectMilestone> milestones)
    {
        project.ProgressPercent =
            CalculateProgressPercent(milestones);

        project.MilestoneDate =
            ComputeProjectMilestoneDate(milestones);
    }

    private static void ThrowValidation(
        string field,
        string message)
    {
        throw new ApiValidationException([
            new ApiFieldError(field, [message])
        ]);
    }
}
