using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.Persistence.Configurations;

public sealed class SubmissionUploadSessionConfiguration : IEntityTypeConfiguration<SubmissionUploadSession>
{
    public void Configure(EntityTypeBuilder<SubmissionUploadSession> builder)
    {
        builder.ToTable("submission_upload_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProjectId).IsRequired();
        builder.Property(x => x.RequirementId).IsRequired();
        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.VersionId).IsRequired();
        builder.Property(x => x.ExpectedVersionNumber).IsRequired();
        builder.Property(x => x.TemporaryObjectKey).HasMaxLength(SubmissionConstants.ObjectKeyMaxLength).IsRequired();
        builder.Property(x => x.FinalObjectKey).HasMaxLength(SubmissionConstants.ObjectKeyMaxLength).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(SubmissionConstants.FileNameMaxLength).IsRequired();
        builder.Property(x => x.FileExtension).HasMaxLength(SubmissionConstants.FileExtensionMaxLength).IsRequired();
        builder.Property(x => x.ExpectedContentType).HasMaxLength(SubmissionConstants.ContentTypeMaxLength).IsRequired();
        builder.Property(x => x.DeclaredFileSizeBytes).IsRequired();
        builder.Property(x => x.ExpectedMaxFileSizeBytes).IsRequired();
        builder.Property(x => x.SubmissionNote).HasMaxLength(SubmissionConstants.SubmissionNoteMaxLength);
        builder.Property(x => x.CreatedBy).IsRequired();
        builder.Property(x => x.CreatedByName).HasMaxLength(SubmissionConstants.UserDisplayNameMaxLength).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ActiveSlot).HasMaxLength(16);
        builder.Property(x => x.ExpiresAt).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnType("datetime(6)");
        builder.Property(x => x.FailureReason).HasMaxLength(SubmissionConstants.UploadFailureReasonMaxLength);
        builder.HasOne<SubmissionRequirement>()
            .WithMany()
            .HasForeignKey(x => x.RequirementId)
            .HasConstraintName("fk_upload_session_requirement")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ProjectId, x.RequirementId, x.Status }).HasDatabaseName("ix_submission_upload_sessions_project_requirement_status");
        builder.HasIndex(x => new { x.ProjectId, x.RequirementId, x.ExpectedVersionNumber, x.ActiveSlot }).IsUnique().HasDatabaseName("ux_submission_upload_sessions_active_version");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_submission_upload_sessions_expires_at");
    }
}
