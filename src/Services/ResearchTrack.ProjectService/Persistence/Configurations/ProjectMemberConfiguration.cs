using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.ProjectService.Domain;

namespace ResearchTrack.ProjectService.Persistence.Configurations;

public sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).ValueGeneratedNever();
        builder.Property(member => member.ProjectId).IsRequired();
        builder.Property(member => member.UserId).IsRequired();
        builder.Property(member => member.MemberRole).HasMaxLength(32).IsRequired();
        builder.Property(member => member.FirstName).HasMaxLength(500).IsRequired();
        builder.Property(member => member.LastName).HasMaxLength(500).IsRequired();
        builder.Property(member => member.Email).HasMaxLength(320).IsRequired();
        builder.Property(member => member.RegistrationNumber).HasMaxLength(100);
        builder.Property(member => member.CreatedAt).IsRequired();
        builder.Property(member => member.UpdatedAt).IsRequired();

        builder.HasIndex(member => new { member.ProjectId, member.UserId })
            .IsUnique()
            .HasDatabaseName("ux_project_members_project_user");
        builder.HasIndex(member => member.ProjectId)
            .HasDatabaseName("ix_project_members_project_id");
        builder.HasIndex(member => new { member.UserId, member.MemberRole })
            .HasDatabaseName("ix_project_members_user_role");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(member => member.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
