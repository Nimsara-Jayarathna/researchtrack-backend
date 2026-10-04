namespace ResearchTrack.SubmissionService.Infrastructure;

public sealed record ProjectStudentContext(
    Guid Id,
    string DisplayName,
    string Email,
    string? RegistrationNumber);

public sealed record ProjectSubmissionContext(
    ProjectStudentContext? Leader,
    IReadOnlyList<ProjectStudentContext> Students)
{
    public ProjectStudentContext? FindStudent(Guid studentId) =>
        Students.FirstOrDefault(x => x.Id == studentId);
}
