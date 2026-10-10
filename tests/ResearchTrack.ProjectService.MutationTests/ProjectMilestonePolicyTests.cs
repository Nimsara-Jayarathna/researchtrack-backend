using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Features.Projects;

namespace ResearchTrack.ProjectService.MutationTests;

public sealed class ProjectMilestonePolicyTests
{
    [Theory]
    [InlineData(" planned ", ProjectMilestoneStatuses.Planned)]
    [InlineData("in_progress", ProjectMilestoneStatuses.InProgress)]
    [InlineData(" completed ", ProjectMilestoneStatuses.Completed)]
    [InlineData("missed", ProjectMilestoneStatuses.Missed)]
    [InlineData("cancelled", ProjectMilestoneStatuses.Cancelled)]
    public void NormalizeAndValidateStatus_NormalizesSupportedValues(string input, string expected)
    {
        Assert.Equal(expected, ProjectMilestonePolicy.NormalizeAndValidateStatus(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UNKNOWN")]
    [InlineData("complete")]
    public void NormalizeAndValidateStatus_RejectsMissingOrUnsupportedValues(string? input)
    {
        var exception = Assert.Throws<ApiValidationException>(
            () => ProjectMilestonePolicy.NormalizeAndValidateStatus(input));

        Assert.Equal("status", Assert.Single(exception.FieldErrors).Field);
    }

    [Theory]
    [InlineData(ProjectMilestoneStatuses.Planned)]
    [InlineData(ProjectMilestoneStatuses.InProgress)]
    public void ValidateDueDateForStatus_RejectsPastDateForOpenMilestones(string status)
    {
        var today = new DateOnly(2026, 10, 9);

        var exception = Assert.Throws<ApiValidationException>(() =>
            ProjectMilestonePolicy.ValidateDueDateForStatus(today.AddDays(-1), status, today));

        Assert.Equal("dueDate", Assert.Single(exception.FieldErrors).Field);
    }

    [Theory]
    [InlineData(ProjectMilestoneStatuses.Planned)]
    [InlineData(ProjectMilestoneStatuses.InProgress)]
    public void ValidateDueDateForStatus_AcceptsTodayAndFutureForOpenMilestones(string status)
    {
        var today = new DateOnly(2026, 10, 9);

        ProjectMilestonePolicy.ValidateDueDateForStatus(today, status, today);
        ProjectMilestonePolicy.ValidateDueDateForStatus(today.AddDays(1), status, today);
    }

    [Theory]
    [InlineData(ProjectMilestoneStatuses.Completed)]
    [InlineData(ProjectMilestoneStatuses.Missed)]
    [InlineData(ProjectMilestoneStatuses.Cancelled)]
    public void ValidateDueDateForStatus_AllowsHistoricalDatesForTerminalMilestones(string status)
    {
        var today = new DateOnly(2026, 10, 9);

        ProjectMilestonePolicy.ValidateDueDateForStatus(today.AddDays(-30), status, today);
    }

    [Fact]
    public void ValidateStatusTransition_AllowsIdempotentTransition()
    {
        ProjectMilestonePolicy.ValidateStatusTransition(
            ProjectMilestoneStatuses.Completed,
            ProjectMilestoneStatuses.Completed);
    }

    [Theory]
    [InlineData(ProjectMilestoneStatuses.Planned)]
    [InlineData(ProjectMilestoneStatuses.InProgress)]
    [InlineData(ProjectMilestoneStatuses.Missed)]
    [InlineData(ProjectMilestoneStatuses.Cancelled)]
    public void ValidateStatusTransition_CompletedMilestoneCannotMoveElsewhere(string nextStatus)
    {
        Assert.Throws<ApiValidationException>(() =>
            ProjectMilestonePolicy.ValidateStatusTransition(
                ProjectMilestoneStatuses.Completed,
                nextStatus));
    }

    [Theory]
    [InlineData(ProjectMilestoneStatuses.Missed, ProjectMilestoneStatuses.Planned)]
    [InlineData(ProjectMilestoneStatuses.Missed, ProjectMilestoneStatuses.InProgress)]
    [InlineData(ProjectMilestoneStatuses.Cancelled, ProjectMilestoneStatuses.Planned)]
    [InlineData(ProjectMilestoneStatuses.Cancelled, ProjectMilestoneStatuses.InProgress)]
    public void ValidateStatusTransition_TerminalMilestoneCannotReopen(string current, string next)
    {
        Assert.Throws<ApiValidationException>(() =>
            ProjectMilestonePolicy.ValidateStatusTransition(current, next));
    }

    [Theory]
    [InlineData(ProjectMilestoneStatuses.Planned, ProjectMilestoneStatuses.InProgress)]
    [InlineData(ProjectMilestoneStatuses.Planned, ProjectMilestoneStatuses.Completed)]
    [InlineData(ProjectMilestoneStatuses.InProgress, ProjectMilestoneStatuses.Completed)]
    [InlineData(ProjectMilestoneStatuses.Missed, ProjectMilestoneStatuses.Cancelled)]
    public void ValidateStatusTransition_AllowsSupportedForwardOrTerminalTransitions(string current, string next)
    {
        ProjectMilestonePolicy.ValidateStatusTransition(current, next);
    }

    [Fact]
    public void ValidateChronologyWithPrevious_RejectsDateBeforePreviousMilestone()
    {
        var previous = new DateOnly(2026, 11, 1);

        var exception = Assert.Throws<ApiValidationException>(() =>
            ProjectMilestonePolicy.ValidateChronologyWithPrevious(previous, previous.AddDays(-1)));

        Assert.Equal("dueDate", Assert.Single(exception.FieldErrors).Field);
    }

    [Fact]
    public void ValidateChronologyWithPrevious_AcceptsSameOrLaterDate()
    {
        var previous = new DateOnly(2026, 11, 1);

        ProjectMilestonePolicy.ValidateChronologyWithPrevious(previous, previous);
        ProjectMilestonePolicy.ValidateChronologyWithPrevious(previous, previous.AddDays(1));
        ProjectMilestonePolicy.ValidateChronologyWithPrevious(null, previous.AddDays(-100));
    }

    [Fact]
    public void ValidateChronologyForUpdate_EnforcesNeighbourBoundariesBySequence()
    {
        var first = Milestone(1, new DateOnly(2026, 11, 1));
        var target = Milestone(2, new DateOnly(2026, 11, 10));
        var last = Milestone(3, new DateOnly(2026, 11, 20));
        var milestones = new[] { last, first, target };

        Assert.Throws<ApiValidationException>(() =>
            ProjectMilestonePolicy.ValidateChronologyForUpdate(
                milestones, target.Id, first.DueDate.AddDays(-1)));

        Assert.Throws<ApiValidationException>(() =>
            ProjectMilestonePolicy.ValidateChronologyForUpdate(
                milestones, target.Id, last.DueDate.AddDays(1)));

        ProjectMilestonePolicy.ValidateChronologyForUpdate(
            milestones, target.Id, new DateOnly(2026, 11, 15));
    }

    [Fact]
    public void ValidateChronologyForUpdate_DoesNothingForUnknownMilestone()
    {
        ProjectMilestonePolicy.ValidateChronologyForUpdate(
            [Milestone(1, new DateOnly(2026, 11, 1))],
            Guid.NewGuid(),
            new DateOnly(2020, 1, 1));
    }

    [Fact]
    public void CalculateProgressPercent_ExcludesCancelledAndRoundsAwayFromZero()
    {
        var milestones = new[]
        {
            Milestone(1, new DateOnly(2026, 11, 1), ProjectMilestoneStatuses.Completed),
            Milestone(2, new DateOnly(2026, 11, 2), ProjectMilestoneStatuses.Planned),
            Milestone(3, new DateOnly(2026, 11, 3), ProjectMilestoneStatuses.Planned),
            Milestone(4, new DateOnly(2026, 11, 4), ProjectMilestoneStatuses.Cancelled)
        };

        Assert.Equal(33, ProjectMilestonePolicy.CalculateProgressPercent(milestones));
    }

    [Fact]
    public void CalculateProgressPercent_ReturnsZeroWhenAllMilestonesAreCancelled()
    {
        Assert.Equal(
            0,
            ProjectMilestonePolicy.CalculateProgressPercent([
                Milestone(1, new DateOnly(2026, 11, 1), ProjectMilestoneStatuses.Cancelled)
            ]));
    }

    [Fact]
    public void ComputeProjectMilestoneDate_ReturnsEarliestOpenDueDateOnly()
    {
        var milestones = new[]
        {
            Milestone(1, new DateOnly(2026, 10, 20), ProjectMilestoneStatuses.Completed),
            Milestone(2, new DateOnly(2026, 11, 10), ProjectMilestoneStatuses.InProgress),
            Milestone(3, new DateOnly(2026, 11, 5), ProjectMilestoneStatuses.Planned),
            Milestone(4, new DateOnly(2026, 10, 25), ProjectMilestoneStatuses.Missed)
        };

        Assert.Equal(
            new DateOnly(2026, 11, 5),
            ProjectMilestonePolicy.ComputeProjectMilestoneDate(milestones));
    }

    [Fact]
    public void ComputeProjectMilestoneDate_ReturnsNullWithoutOpenMilestones()
    {
        Assert.Null(ProjectMilestonePolicy.ComputeProjectMilestoneDate([
            Milestone(1, new DateOnly(2026, 11, 1), ProjectMilestoneStatuses.Completed),
            Milestone(2, new DateOnly(2026, 11, 2), ProjectMilestoneStatuses.Cancelled)
        ]));
    }

    [Fact]
    public void ApplyAggregates_UpdatesProjectProgressAndNextMilestoneDate()
    {
        var project = new Project
        {
            Title = "ResearchTrack",
            Summary = "Summary",
            Batch = "Y3",
            Semester = ProjectSemesters.Semester1,
            LifecycleStatus = "ACTIVE"
        };
        var milestones = new[]
        {
            Milestone(1, new DateOnly(2026, 11, 1), ProjectMilestoneStatuses.Completed),
            Milestone(2, new DateOnly(2026, 11, 15), ProjectMilestoneStatuses.Planned)
        };

        ProjectMilestonePolicy.ApplyAggregates(project, milestones);

        Assert.Equal(50, project.ProgressPercent);
        Assert.Equal(new DateOnly(2026, 11, 15), project.MilestoneDate);
    }

    private static ProjectMilestone Milestone(
        int sequence,
        DateOnly dueDate,
        string status = ProjectMilestoneStatuses.Planned) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        Title = $"Milestone {sequence}",
        DueDate = dueDate,
        Status = status,
        SequenceNo = sequence
    };
}
