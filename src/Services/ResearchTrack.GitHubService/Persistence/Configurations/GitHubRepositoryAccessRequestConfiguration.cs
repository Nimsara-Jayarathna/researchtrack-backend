using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubRepositoryAccessRequestConfiguration
    : IEntityTypeConfiguration<GitHubRepositoryAccessRequest>
{
    public void Configure(EntityTypeBuilder<GitHubRepositoryAccessRequest> builder)
    {
        builder.ToTable("github_repository_access_requests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.ProjectId).IsRequired();
        builder.Property(request => request.InitiatingUserId).IsRequired();
        builder.Property(request => request.RequestedOwner).HasMaxLength(39).IsRequired();
        builder.Property(request => request.RequestedRepositoryName).HasMaxLength(100).IsRequired();
        builder.Property(request => request.RequestedFullName).HasMaxLength(140).IsRequired();
        builder.Property(request => request.RequestTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(request => request.FlowType).HasMaxLength(32).IsRequired();
        builder.Property(request => request.Status).HasMaxLength(32).IsRequired();
        builder.Property(request => request.FailureCode).HasMaxLength(128);
        builder.Property(request => request.Version).IsConcurrencyToken().IsRequired();

        builder.HasIndex(request => request.RequestTokenHash)
            .IsUnique()
            .HasDatabaseName("ux_github_repository_access_requests_token_hash");
        builder.HasIndex(request => new { request.Status, request.ExpiresAt })
            .HasDatabaseName("ix_github_repository_access_requests_status_expiry");
        builder.HasIndex(request => request.ProjectId)
            .HasDatabaseName("ix_github_repository_access_requests_project_id");
    }
}
