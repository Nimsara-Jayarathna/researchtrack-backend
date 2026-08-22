using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.AuthService.Domain;

namespace ResearchTrack.AuthService.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .ValueGeneratedNever();

        builder.Property(user => user.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(user => user.FirstName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(user => user.LastName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(user => user.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(user => user.RegistrationNumber)
            .HasMaxLength(100);

        builder.Property(user => user.CreatedAt)
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("ux_users_email");

        builder.HasIndex(user => user.RegistrationNumber)
            .IsUnique()
            .HasDatabaseName("ux_users_registration_number");
    }
}
