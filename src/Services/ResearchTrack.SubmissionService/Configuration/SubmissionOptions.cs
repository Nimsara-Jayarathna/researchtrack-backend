namespace ResearchTrack.SubmissionService.Configuration;

public sealed class SubmissionOptions
{
    public const string SectionName = "Submission";

    public int DependencyTimeoutSeconds { get; init; } = 10;

    public int MaxFileNameLength { get; init; } = 255;

    public string[] AllowedFileTypes { get; init; } = ["pdf", "docx", "pptx", "zip"];

    public int UploadSessionLifetimeMinutes { get; init; } = 10;

    public int CleanupIntervalMinutes { get; init; } = 15;
}
