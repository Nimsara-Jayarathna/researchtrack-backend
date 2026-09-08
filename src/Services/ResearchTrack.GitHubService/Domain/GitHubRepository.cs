namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubRepository
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public long GitHubRepositoryId { get; set; }
    public required string FullName { get; set; }
    public required string Name { get; set; }
    public required string OwnerLogin { get; set; }
    public string? DefaultBranch { get; set; }
    public required string Url { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
