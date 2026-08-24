using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.AuthService.Domain;

namespace ResearchTrack.AuthService.Persistence.Configurations;

public sealed class EmailOtpConfiguration : IEntityTypeConfiguration<EmailOtp>
{
    public void Configure(EntityTypeBuilder<EmailOtp> builder)
    {
        builder.ToTable("email_otps");
        builder.HasKey(otp => otp.Id);

        builder.Property(otp => otp.Id)
            .ValueGeneratedNever();

        builder.Property(otp => otp.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(otp => otp.OtpHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(otp => otp.ExpiresAt)
            .IsRequired();

        builder.Property(otp => otp.CreatedAt)
            .IsRequired();

        builder.HasIndex(otp => otp.Email)
            .HasDatabaseName("ix_email_otps_email");

        builder.HasIndex(otp => otp.ExpiresAt)
            .HasDatabaseName("ix_email_otps_expires_at");
    }
}
