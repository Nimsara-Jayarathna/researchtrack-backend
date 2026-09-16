using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubSyncRunConfiguration : IEntityTypeConfiguration<GitHubSyncRun>
{
    public void Configure(EntityTypeBuilder<GitHubSyncRun> builder)
    {
        builder.ToTable("github_sync_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(128);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2048);
        builder.HasIndex(x => new { x.RepositoryLinkId, x.StartedAt });
    }
}
