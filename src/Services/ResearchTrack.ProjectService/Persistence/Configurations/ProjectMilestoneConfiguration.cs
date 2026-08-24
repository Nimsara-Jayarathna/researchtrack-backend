using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.ProjectService.Domain;

namespace ResearchTrack.ProjectService.Persistence.Configurations;

public sealed class ProjectMilestoneConfiguration : IEntityTypeConfiguration<ProjectMilestone>
{
    public void Configure(EntityTypeBuilder<ProjectMilestone> builder)
    {
        builder.ToTable("project_milestones");
        builder.HasKey(milestone => milestone.Id);
        builder.Property(milestone => milestone.Id).ValueGeneratedNever();
        builder.Property(milestone => milestone.ProjectId).IsRequired();
        builder.Property(milestone => milestone.Title).HasMaxLength(40).IsRequired();
        builder.Property(milestone => milestone.Description).HasMaxLength(250);
        builder.Property(milestone => milestone.DueDate)
            .HasConversion(
                value => value.ToDateTime(TimeOnly.MinValue),
                value => DateOnly.FromDateTime(value))
            .HasColumnType("date")
            .IsRequired();
        builder.Property(milestone => milestone.Status).HasMaxLength(32).IsRequired();
        builder.Property(milestone => milestone.SequenceNo).IsRequired();
        builder.Property(milestone => milestone.CreatedByUserId).IsRequired();
        builder.Property(milestone => milestone.CreatedAt).IsRequired();
        builder.Property(milestone => milestone.UpdatedAt).IsRequired();

        builder.HasIndex(milestone => milestone.ProjectId)
            .HasDatabaseName("ix_project_milestones_project_id");
        builder.HasIndex(milestone => new { milestone.ProjectId, milestone.SequenceNo })
            .IsUnique()
            .HasDatabaseName("ux_project_milestones_project_sequence");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(milestone => milestone.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
