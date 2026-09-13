using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubPullRequestConfiguration : IEntityTypeConfiguration<GitHubPullRequest>
{
    public void Configure(EntityTypeBuilder<GitHubPullRequest> builder)
    {
        builder.ToTable("github_pull_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Body).HasColumnType("longtext");
        builder.Property(x => x.State).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AuthorLogin).HasMaxLength(255);
        builder.Property(x => x.SourceBranch).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SourceSha).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetBranch).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TargetSha).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MergeCommitSha).HasMaxLength(64);
        builder.Property(x => x.HtmlUrl).HasMaxLength(2048).IsRequired();
        builder.HasIndex(x => new { x.RepositoryLinkId, x.GitHubPullRequestId }).IsUnique();
        builder.HasIndex(x => new { x.RepositoryLinkId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.RepositoryLinkId, x.UpdatedAt });
    }
}
