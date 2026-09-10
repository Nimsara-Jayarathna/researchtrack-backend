using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubAccessSourceConfiguration : IEntityTypeConfiguration<GitHubAccessSource>
{
    public void Configure(EntityTypeBuilder<GitHubAccessSource> builder)
    {
        builder.ToTable("github_access_sources");
        builder.HasKey(source => source.Id);
        builder.Property(source => source.Id).ValueGeneratedNever();
        builder.Property(source => source.ProjectId).IsRequired();
        builder.Property(source => source.CreatedByUserId).IsRequired();
        builder.Property(source => source.OwnerLogin).HasMaxLength(255).IsRequired();
        builder.Property(source => source.OwnerType).HasMaxLength(32).IsRequired();
        builder.Property(source => source.AccessType).HasMaxLength(32).IsRequired();
        builder.Property(source => source.Active).IsRequired();
        builder.Property(source => source.ActiveRepositoryKey).HasMaxLength(128);
        builder.Property(source => source.CreatedAt).IsRequired();
        builder.Property(source => source.UpdatedAt).IsRequired();

        builder.HasIndex(source => source.ProjectId)
            .HasDatabaseName("ix_github_access_sources_project_id");
        builder.HasIndex(source => source.ActiveRepositoryKey)
            .IsUnique()
            .HasDatabaseName("ux_github_access_sources_active_repository");
    }
}
