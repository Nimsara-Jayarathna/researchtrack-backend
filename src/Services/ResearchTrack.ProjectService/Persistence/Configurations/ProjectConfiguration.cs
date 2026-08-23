using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.ProjectService.Domain;

namespace ResearchTrack.ProjectService.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();
        builder.Property(project => project.Title).HasMaxLength(40).IsRequired();
        builder.Property(project => project.Summary).HasMaxLength(250).IsRequired();
        builder.Property(project => project.Batch).HasMaxLength(32).IsRequired();
        builder.Property(project => project.Semester).HasMaxLength(32).IsRequired();
        builder.Property(project => project.LifecycleStatus).HasMaxLength(32).IsRequired();
        builder.Property(project => project.ProgressPercent).IsRequired();
        builder.Property(project => project.SupervisorUserId).IsRequired();
        builder.Property(project => project.LeaderStudentUserId);
        builder.Property(project => project.MilestoneDate)
            .HasConversion(
                value => value.HasValue
                    ? value.Value.ToDateTime(TimeOnly.MinValue)
                    : (DateTime?)null,
                value => value.HasValue
                    ? DateOnly.FromDateTime(value.Value)
                    : (DateOnly?)null)
            .HasColumnType("date");
        builder.Property(project => project.LastActivityAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP(6)")
            .IsRequired();
        builder.Property(project => project.CreatedAt).IsRequired();
        builder.Property(project => project.UpdatedAt).IsRequired();

        builder.HasIndex(project => project.SupervisorUserId)
            .HasDatabaseName("ix_projects_supervisor_user_id");
        builder.HasIndex(project => new { project.SupervisorUserId, project.CreatedAt })
            .HasDatabaseName("ix_projects_supervisor_created_at");
    }
}
