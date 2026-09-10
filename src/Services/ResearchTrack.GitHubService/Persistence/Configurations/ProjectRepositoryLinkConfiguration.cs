using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class ProjectRepositoryLinkConfiguration : IEntityTypeConfiguration<ProjectRepositoryLink>
{
    public void Configure(EntityTypeBuilder<ProjectRepositoryLink> builder)
    {
        builder.ToTable("project_repository_links");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.Property(link => link.ProjectId).IsRequired();
        builder.Property(link => link.SourceId).IsRequired();
        builder.Property(link => link.GitHubRepositoryId).IsRequired();
        builder.Property(link => link.GitHubRepoId).IsRequired();
        builder.Property(link => link.LinkedByUserId).IsRequired();
        builder.Property(link => link.AccessType).HasMaxLength(32).IsRequired();
        builder.Property(link => link.FullName).HasMaxLength(512).IsRequired();
        builder.Property(link => link.Name).HasMaxLength(255).IsRequired();
        builder.Property(link => link.CustomName).HasMaxLength(255);
        builder.Property(link => link.OwnerLogin).HasMaxLength(255).IsRequired();
        builder.Property(link => link.DefaultBranch).HasMaxLength(255);
        builder.Property(link => link.Url).HasMaxLength(2048).IsRequired();
        builder.Property(link => link.Active).IsRequired();
        builder.Property(link => link.Primary).IsRequired();
        builder.Property(link => link.Enabled).IsRequired();
        builder.Property(link => link.ActiveRepositoryKey).HasMaxLength(96);
        builder.Property(link => link.PrimaryProjectKey).HasMaxLength(32);
        builder.Property(link => link.LinkedAt).IsRequired();
        builder.Property(link => link.LastSyncedAt);
        builder.Property(link => link.SyncStatus).HasMaxLength(32).IsRequired();
        builder.Property(link => link.UpdatedAt).IsRequired();

        builder.HasIndex(link => new { link.ProjectId, link.Active })
            .HasDatabaseName("ix_project_repository_links_project_active");
        builder.HasIndex(link => link.SourceId)
            .HasDatabaseName("ix_project_repository_links_source_id");
        builder.HasIndex(link => link.ActiveRepositoryKey)
            .IsUnique()
            .HasDatabaseName("ux_project_repository_links_active_repository");
        builder.HasIndex(link => link.PrimaryProjectKey)
            .IsUnique()
            .HasDatabaseName("ux_project_repository_links_primary_project");

        builder.HasOne<GitHubAccessSource>()
            .WithMany()
            .HasForeignKey(link => link.SourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitHubRepository>()
            .WithMany()
            .HasForeignKey(link => link.GitHubRepositoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
