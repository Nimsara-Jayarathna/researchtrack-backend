namespace ResearchTrack.ProjectService.Contracts;

public sealed record ProjectSummaryResponse(
    Guid Id,
    string Title,
    string Summary,
    string LifecycleStatus,
    string Batch,
    string Semester,
    DateOnly? MilestoneDate,
    int ProgressPercent,
    int MemberCount);
