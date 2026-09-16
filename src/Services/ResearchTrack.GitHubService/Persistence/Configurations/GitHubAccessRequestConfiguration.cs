using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubAccessRequestConfiguration : IEntityTypeConfiguration<GitHubAccessRequest>
{
    public void Configure(EntityTypeBuilder<GitHubAccessRequest> builder)
    {
        builder.ToTable("github_access_requests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.ProjectId).IsRequired();
        builder.Property(request => request.ProjectTitle).HasMaxLength(255).IsRequired();
        builder.Property(request => request.TargetOwnerLogin).HasMaxLength(100).IsRequired();
        builder.Property(request => request.RequestedByUserId).IsRequired();
        builder.Property(request => request.TokenNonce).HasMaxLength(64).IsRequired();
        builder.Property(request => request.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(request => request.ResultNonce).HasMaxLength(64);
        builder.Property(request => request.ResultTokenHash).HasMaxLength(64);
        builder.Property(request => request.Status).HasMaxLength(32).IsRequired();
        builder.Property(request => request.PendingProjectKey).HasMaxLength(32);
        builder.Property(request => request.ErrorCode).HasMaxLength(128);
        builder.Property(request => request.CreatedAt).IsRequired();
        builder.Property(request => request.ExpiresAt).IsRequired();

        builder.HasIndex(request => request.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_github_access_requests_token_hash");
        builder.HasIndex(request => request.ResultTokenHash)
            .IsUnique()
            .HasDatabaseName("ux_github_access_requests_result_token_hash");
        builder.HasIndex(request => new { request.ProjectId, request.Status })
            .HasDatabaseName("ix_github_access_requests_project_status");
        builder.HasIndex(request => request.PendingProjectKey)
            .IsUnique()
            .HasDatabaseName("ux_github_access_requests_pending_project");
        builder.HasIndex(request => request.ExpiresAt)
            .HasDatabaseName("ix_github_access_requests_expires_at");
    }
}
