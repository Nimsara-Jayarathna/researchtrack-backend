namespace ResearchTrack.GitHubService.Domain;

public sealed class ProjectRepositoryLink
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid SourceId { get; set; }
    public Guid GitHubRepositoryId { get; set; }
    public long GitHubRepoId { get; set; }
    public Guid LinkedByUserId { get; set; }
    public required string AccessType { get; set; }
    public required string FullName { get; set; }
    public required string Name { get; set; }
    public string? CustomName { get; set; }
    public required string OwnerLogin { get; set; }
    public string? DefaultBranch { get; set; }
    public required string Url { get; set; }
    public bool Active { get; set; }
    public bool Primary { get; set; }
    public bool Enabled { get; set; }
    public string? ActiveRepositoryKey { get; set; }
    public string? PrimaryProjectKey { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public required string SyncStatus { get; set; }
    public DateTime UpdatedAt { get; set; }
}
