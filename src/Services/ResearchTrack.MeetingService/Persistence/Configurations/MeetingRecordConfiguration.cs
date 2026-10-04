using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Persistence.Configurations;

public sealed class MeetingRecordConfiguration : IEntityTypeConfiguration<MeetingRecord>
{
    public void Configure(EntityTypeBuilder<MeetingRecord> builder)
    {
        builder.ToTable("meeting_records");
        builder.HasKey(record => record.Id);

        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.ProjectId).IsRequired();
        builder.Property(record => record.MeetingDate).HasColumnType("date").IsRequired();
        builder.Property(record => record.DurationMinutes).IsRequired();
        builder.Property(record => record.DiscussionSummary)
            .HasMaxLength(MeetingRecordConstants.DiscussionSummaryMaxLength)
            .IsRequired();
        builder.Property(record => record.DiscussionDetails)
            .HasMaxLength(MeetingRecordConstants.DiscussionDetailsMaxLength);
        builder.Property(record => record.ChannelId);
        builder.Property(record => record.AddedBy).IsRequired();
        builder.Property(record => record.AddedByName)
            .HasMaxLength(MeetingRecordConstants.UserDisplayNameMaxLength)
            .IsRequired();
        builder.Property(record => record.AddedByRole).HasMaxLength(32).IsRequired();
        builder.Property(record => record.Status).HasMaxLength(32).IsRequired();
        builder.Property(record => record.ApprovedBy);
        builder.Property(record => record.ApprovedByName)
            .HasMaxLength(MeetingRecordConstants.UserDisplayNameMaxLength);
        builder.Property(record => record.ApprovedAt).HasColumnType("datetime(6)");
        builder.Property(record => record.CreatedAt).HasColumnType("datetime(6)").IsRequired();
        builder.Property(record => record.UpdatedAt).HasColumnType("datetime(6)");

        builder.HasOne<MeetingChannel>()
            .WithMany()
            .HasForeignKey(record => record.ChannelId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(record => record.ProjectId)
            .HasDatabaseName("ix_meeting_records_project_id");
        builder.HasIndex(record => new
            {
                record.ProjectId,
                record.Status,
                record.MeetingDate,
                record.CreatedAt
            })
            .HasDatabaseName("ix_meeting_records_project_status_date_created_at");
        builder.HasIndex(record => record.ChannelId)
            .HasDatabaseName("ix_meeting_records_channel_id");
    }
}
