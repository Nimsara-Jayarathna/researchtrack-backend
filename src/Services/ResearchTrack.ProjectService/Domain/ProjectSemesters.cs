namespace ResearchTrack.ProjectService.Domain;

public static class ProjectSemesters
{
    public const string Semester1 = "Semester 1";
    public const string Semester2 = "Semester 2";

    private static readonly HashSet<string> Supported =
        new(StringComparer.Ordinal)
        {
            Semester1,
            Semester2
        };

    public static bool IsSupported(string? semester) =>
        semester is not null && Supported.Contains(semester);
}
