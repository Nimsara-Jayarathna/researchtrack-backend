using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Persistence.Configurations;

public sealed class MeetingChannelConfiguration : IEntityTypeConfiguration<MeetingChannel>
{
    public void Configure(EntityTypeBuilder<MeetingChannel> builder)
    {
        builder.ToTable("meeting_channels");
        builder.HasKey(channel => channel.Id);

        builder.Property(channel => channel.Id).ValueGeneratedNever();
        builder.Property(channel => channel.ProjectId).IsRequired();
        builder.Property(channel => channel.Platform).HasMaxLength(32).IsRequired();
        builder.Property(channel => channel.ChannelName)
            .HasMaxLength(MeetingChannelConstants.ChannelNameMaxLength)
            .IsRequired();
        builder.Property(channel => channel.LinkOrIdentifier)
            .HasMaxLength(MeetingChannelConstants.LinkMaxLength)
            .IsRequired();
        builder.Property(channel => channel.AddedBy).IsRequired();
        builder.Property(channel => channel.AddedByName)
            .HasMaxLength(MeetingChannelConstants.UserDisplayNameMaxLength)
            .IsRequired();
        builder.Property(channel => channel.AddedByRole).HasMaxLength(32).IsRequired();
        builder.Property(channel => channel.Status).HasMaxLength(32).IsRequired();
        builder.Property(channel => channel.ApprovedBy);
        builder.Property(channel => channel.ApprovedByName)
            .HasMaxLength(MeetingChannelConstants.UserDisplayNameMaxLength);
        builder.Property(channel => channel.ApprovedAt).HasColumnType("datetime(6)");
        builder.Property(channel => channel.CreatedAt).HasColumnType("datetime(6)").IsRequired();
        builder.Property(channel => channel.UpdatedAt).HasColumnType("datetime(6)");

        builder.HasIndex(channel => channel.ProjectId)
            .HasDatabaseName("ix_meeting_channels_project_id");
        builder.HasIndex(channel => new { channel.ProjectId, channel.Status, channel.CreatedAt })
            .HasDatabaseName("ix_meeting_channels_project_status_created_at");
    }
}
