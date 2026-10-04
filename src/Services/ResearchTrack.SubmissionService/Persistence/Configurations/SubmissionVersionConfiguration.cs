using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.Persistence.Configurations;

public sealed class SubmissionVersionConfiguration : IEntityTypeConfiguration<SubmissionVersion>
{
    public void Configure(EntityTypeBuilder<SubmissionVersion> builder)
    {
        builder.ToTable("submission_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.VersionNumber).IsRequired();
        builder.Property(x => x.ObjectKey).HasMaxLength(SubmissionConstants.ObjectKeyMaxLength).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(SubmissionConstants.FileNameMaxLength).IsRequired();
        builder.Property(x => x.FileExtension).HasMaxLength(SubmissionConstants.FileExtensionMaxLength).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(SubmissionConstants.ContentTypeMaxLength).IsRequired();
        builder.Property(x => x.FileSizeBytes).IsRequired();
        builder.Property(x => x.ObjectETag).HasMaxLength(255);
        builder.Property(x => x.UploadedBy).IsRequired();
        builder.Property(x => x.UploadedByName).HasMaxLength(SubmissionConstants.UserDisplayNameMaxLength).IsRequired();
        builder.Property(x => x.SubmitterRoleSnapshot).HasMaxLength(SubmissionConstants.SubmitterRoleSnapshotMaxLength);
        builder.Property(x => x.ResponsibilityModeSnapshot).HasMaxLength(SubmissionConstants.ResponsibilityModeMaxLength);
        builder.Property(x => x.SubmissionNote).HasMaxLength(SubmissionConstants.SubmissionNoteMaxLength);
        builder.Property(x => x.SubmittedAt).HasColumnType("datetime(6)").IsRequired();
        builder.Property(x => x.IsLate).IsRequired();
        builder.HasOne<ResearchSubmission>()
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .HasConstraintName("fk_submission_version_submission")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.SubmissionId, x.VersionNumber }).IsUnique().HasDatabaseName("ux_submission_versions_submission_version");
    }
}
