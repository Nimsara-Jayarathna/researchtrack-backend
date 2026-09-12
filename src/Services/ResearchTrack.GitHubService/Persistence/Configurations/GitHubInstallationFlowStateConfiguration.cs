using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubInstallationFlowStateConfiguration
    : IEntityTypeConfiguration<GitHubInstallationFlowState>
{
    public void Configure(EntityTypeBuilder<GitHubInstallationFlowState> builder)
    {
        builder.ToTable("github_installation_flow_states");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).ValueGeneratedNever();
        builder.Property(state => state.StateHash).HasMaxLength(64).IsRequired();
        builder.Property(state => state.ProjectId).IsRequired();
        builder.Property(state => state.InitiatingUserId).IsRequired();
        builder.Property(state => state.FlowType).HasMaxLength(32).IsRequired();
        builder.Property(state => state.ReturnPath).HasMaxLength(512).IsRequired();
        builder.Property(state => state.CreatedAt).IsRequired();
        builder.Property(state => state.ExpiresAt).IsRequired();
        builder.Property(state => state.PendingInstallationId);
        builder.Property(state => state.AuthorizationStartedAt);

        builder.HasIndex(state => state.StateHash)
            .IsUnique()
            .HasDatabaseName("ux_github_installation_flow_states_hash");
        builder.HasIndex(state => state.ExpiresAt)
            .HasDatabaseName("ix_github_installation_flow_states_expires_at");
        builder.HasIndex(state => new { state.ProjectId, state.InitiatingUserId })
            .HasDatabaseName("ix_github_installation_flow_states_project_user");
    }
}
