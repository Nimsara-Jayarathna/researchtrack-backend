using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.Persistence.Configurations;

public sealed class ResearchSubmissionConfiguration : IEntityTypeConfiguration<ResearchSubmission>
{
    public void Configure(EntityTypeBuilder<ResearchSubmission> builder)
    {
        builder.ToTable("research_submissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProjectId).IsRequired();
        builder.Property(x => x.RequirementId).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CurrentVersionId);
        builder.Property(x => x.VersionCount).IsRequired();
        builder.Property(x => x.LastSubmittedAt).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.ApprovedVersionId);
        builder.Property(x => x.ApprovedAt).HasColumnType("datetime(6)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime(6)");
        builder.HasOne<SubmissionRequirement>()
            .WithMany()
            .HasForeignKey(x => x.RequirementId)
            .HasConstraintName("fk_research_submission_requirement")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ProjectId, x.RequirementId }).IsUnique().HasDatabaseName("ux_research_submissions_project_requirement");
        builder.HasIndex(x => new { x.ProjectId, x.Status }).HasDatabaseName("ix_research_submissions_project_status");
        builder.HasIndex(x => x.RequirementId).HasDatabaseName("ix_research_submissions_requirement_id");
    }
}
