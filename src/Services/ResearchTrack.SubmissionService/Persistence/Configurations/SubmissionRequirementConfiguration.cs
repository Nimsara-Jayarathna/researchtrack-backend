using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.Persistence.Configurations;

public sealed class SubmissionRequirementConfiguration : IEntityTypeConfiguration<SubmissionRequirement>
{
    public void Configure(EntityTypeBuilder<SubmissionRequirement> builder)
    {
        builder.ToTable("submission_requirements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProjectId).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(SubmissionConstants.RequirementTitleMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(SubmissionConstants.RequirementDescriptionMaxLength);
        builder.Property(x => x.DueAt).HasColumnType("datetime(6)");
        builder.Property(x => x.AllowedFileTypes).HasMaxLength(SubmissionConstants.AllowedFileTypesMaxLength).IsRequired();
        builder.Property(x => x.MaxFileSizeBytes).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedBy).IsRequired();
        builder.Property(x => x.CreatedByName).HasMaxLength(SubmissionConstants.UserDisplayNameMaxLength).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime(6)");
        builder.HasIndex(x => new { x.ProjectId, x.Status }).HasDatabaseName("ix_submission_requirements_project_status");
        builder.HasIndex(x => new { x.ProjectId, x.DueAt }).HasDatabaseName("ix_submission_requirements_project_due_at");
    }
}
