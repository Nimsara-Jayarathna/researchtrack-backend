namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubAccessSource
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public long? InstallationId { get; set; }
    public required string OwnerLogin { get; set; }
    public required string OwnerType { get; set; }
    public required string AccessType { get; set; }
    public bool Active { get; set; }
    public string? ActiveRepositoryKey { get; set; }
    public string? ActiveInstallationKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
