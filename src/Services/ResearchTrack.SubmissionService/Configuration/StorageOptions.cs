namespace ResearchTrack.SubmissionService.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string? Endpoint { get; init; }

    public string Bucket { get; init; } = string.Empty;

    public string? AccessKey { get; init; }

    public string? SecretKey { get; init; }

    public string Region { get; init; } = "ap-south-1";

    public long MaximumFileSizeBytes { get; init; } = 10L * 1024L * 1024L;

    public int PresignedUrlExpirySeconds { get; init; } = 300;

    public bool ForcePathStyle { get; init; }
}
