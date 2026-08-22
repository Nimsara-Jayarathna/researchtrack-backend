using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.AuthService.Domain;

namespace ResearchTrack.AuthService.Persistence.Configurations;

public sealed class RegistrationSessionConfiguration : IEntityTypeConfiguration<RegistrationSession>
{
    public void Configure(EntityTypeBuilder<RegistrationSession> builder)
    {
        builder.ToTable("registration_sessions");
        builder.HasKey(session => session.Id);

        builder.Property(session => session.Id)
            .ValueGeneratedNever();

        builder.Property(session => session.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(session => session.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(session => session.Role)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(session => session.ExpiresAt)
            .IsRequired();

        builder.Property(session => session.CreatedAt)
            .IsRequired();

        builder.HasIndex(session => session.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_registration_sessions_token_hash");

        builder.HasIndex(session => session.ExpiresAt)
            .HasDatabaseName("ix_registration_sessions_expires_at");
    }
}
