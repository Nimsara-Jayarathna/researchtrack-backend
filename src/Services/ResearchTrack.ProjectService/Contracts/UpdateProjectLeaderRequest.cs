namespace ResearchTrack.ProjectService.Contracts;

public sealed record UpdateProjectLeaderRequest(
    Guid? LeaderStudentId);
