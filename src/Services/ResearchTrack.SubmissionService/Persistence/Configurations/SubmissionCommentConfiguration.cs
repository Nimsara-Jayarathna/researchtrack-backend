using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.Persistence.Configurations;

public sealed class SubmissionCommentConfiguration : IEntityTypeConfiguration<SubmissionComment>
{
    public void Configure(EntityTypeBuilder<SubmissionComment> builder)
    {
        builder.ToTable("submission_comments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.VersionId);
        builder.Property(x => x.AuthorId).IsRequired();
        builder.Property(x => x.AuthorName).HasMaxLength(SubmissionConstants.UserDisplayNameMaxLength).IsRequired();
        builder.Property(x => x.AuthorRole).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(SubmissionConstants.CommentMaxLength).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime(6)").IsRequired();

        builder.HasOne<ResearchSubmission>()
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .HasConstraintName("fk_comment_submission")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SubmissionVersion>()
            .WithMany()
            .HasForeignKey(x => x.VersionId)
            .HasConstraintName("fk_comment_version")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SubmissionId, x.CreatedAt })
            .HasDatabaseName("ix_submission_comments_submission_time");
        builder.HasIndex(x => x.VersionId)
            .HasDatabaseName("ix_submission_comments_version");
    }
}
