using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubContributorConfiguration : IEntityTypeConfiguration<GitHubContributor>
{
    public void Configure(EntityTypeBuilder<GitHubContributor> builder)
    {
        builder.ToTable("github_contributors");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Login).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AvatarUrl).HasMaxLength(2048);
        builder.Property(x => x.ProfileUrl).HasMaxLength(2048);
        builder.HasIndex(x => new { x.RepositoryLinkId, x.GitHubUserId }).IsUnique();
        builder.HasIndex(x => new { x.RepositoryLinkId, x.Login });
    }
}
