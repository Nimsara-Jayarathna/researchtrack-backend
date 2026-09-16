using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubCommitConfiguration : IEntityTypeConfiguration<GitHubCommit>
{
    public void Configure(EntityTypeBuilder<GitHubCommit> builder)
    {
        builder.ToTable("github_commits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sha).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Message).HasColumnType("text").IsRequired();
        builder.Property(x => x.AuthorLogin).HasMaxLength(255);
        builder.Property(x => x.AuthorName).HasMaxLength(255);
        builder.Property(x => x.AuthorEmail).HasMaxLength(320);
        builder.Property(x => x.CommitterLogin).HasMaxLength(255);
        builder.Property(x => x.HtmlUrl).HasMaxLength(2048).IsRequired();
        builder.HasIndex(x => new { x.RepositoryLinkId, x.Sha }).IsUnique();
        builder.HasIndex(x => new { x.RepositoryLinkId, x.CommittedAt });
    }
}
