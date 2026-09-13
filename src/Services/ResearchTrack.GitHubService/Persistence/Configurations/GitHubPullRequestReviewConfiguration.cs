using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubPullRequestReviewConfiguration : IEntityTypeConfiguration<GitHubPullRequestReview>
{
    public void Configure(EntityTypeBuilder<GitHubPullRequestReview> builder)
    {
        builder.ToTable("github_pull_request_reviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReviewerLogin).HasMaxLength(255);
        builder.Property(x => x.State).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.PullRequestId, x.GitHubReviewId }).IsUnique();
    }
}
