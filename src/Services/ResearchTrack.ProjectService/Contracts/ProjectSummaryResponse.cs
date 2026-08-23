namespace ResearchTrack.ProjectService.Contracts;

public sealed record ProjectSummaryResponse(
    Guid Id,
    string Title,
    string Summary,
    string LifecycleStatus,
    string Batch,
    string Semester,
    DateOnly? MilestoneDate,
    DateTime? LastActivityAt,
    int ProgressPercent,
    int MemberCount,
    string? SupervisorName);
