using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubBranchConfiguration : IEntityTypeConfiguration<GitHubBranch>
{
    public void Configure(EntityTypeBuilder<GitHubBranch> builder)
    {
        builder.ToTable("github_branches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.HeadSha).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.RepositoryLinkId, x.Name }).IsUnique();
    }
}
