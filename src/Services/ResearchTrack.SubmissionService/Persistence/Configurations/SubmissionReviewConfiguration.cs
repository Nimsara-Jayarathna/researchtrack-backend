using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.Persistence.Configurations;

public sealed class SubmissionReviewConfiguration : IEntityTypeConfiguration<SubmissionReview>
{
    public void Configure(EntityTypeBuilder<SubmissionReview> builder)
    {
        builder.ToTable("submission_reviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.VersionId).IsRequired();
        builder.Property(x => x.Decision).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Feedback).HasMaxLength(SubmissionConstants.ReviewFeedbackMaxLength);
        builder.Property(x => x.ReviewedBy).IsRequired();
        builder.Property(x => x.ReviewedByName).HasMaxLength(SubmissionConstants.UserDisplayNameMaxLength).IsRequired();
        builder.Property(x => x.ReviewedAt).HasColumnType("datetime(6)").IsRequired();

        builder.HasOne<ResearchSubmission>()
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .HasConstraintName("fk_review_submission")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SubmissionVersion>()
            .WithMany()
            .HasForeignKey(x => x.VersionId)
            .HasConstraintName("fk_review_version")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.VersionId)
            .IsUnique()
            .HasDatabaseName("ux_submission_reviews_version");
        builder.HasIndex(x => new { x.SubmissionId, x.ReviewedAt })
            .HasDatabaseName("ix_submission_reviews_submission_time");
    }
}
