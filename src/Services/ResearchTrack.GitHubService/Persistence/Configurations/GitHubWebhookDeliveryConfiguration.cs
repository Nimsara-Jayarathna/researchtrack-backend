using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubWebhookDeliveryConfiguration : IEntityTypeConfiguration<GitHubWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<GitHubWebhookDelivery> builder)
    {
        builder.ToTable("github_webhook_deliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();
        builder.Property(delivery => delivery.DeliveryId).HasMaxLength(128).IsRequired();
        builder.Property(delivery => delivery.EventType).HasMaxLength(64).IsRequired();
        builder.Property(delivery => delivery.Action).HasMaxLength(128);
        builder.Property(delivery => delivery.InstallationId);
        builder.Property(delivery => delivery.GitHubRepositoryId);
        builder.Property(delivery => delivery.PayloadJson).HasColumnType("longtext").IsRequired();
        builder.Property(delivery => delivery.PayloadSha256).HasMaxLength(64).IsRequired();
        builder.Property(delivery => delivery.Status).HasMaxLength(32).IsRequired();
        builder.Property(delivery => delivery.AttemptCount).IsRequired();
        builder.Property(delivery => delivery.ReceivedAt).IsRequired();
        builder.Property(delivery => delivery.ProcessingStartedAt);
        builder.Property(delivery => delivery.ProcessedAt);
        builder.Property(delivery => delivery.NextAttemptAt);
        builder.Property(delivery => delivery.LastError).HasMaxLength(2048);
        builder.Property(delivery => delivery.UpdatedAt).IsRequired();

        builder.HasIndex(delivery => delivery.DeliveryId)
            .IsUnique()
            .HasDatabaseName("ux_github_webhook_deliveries_delivery_id");
        builder.HasIndex(delivery => new { delivery.Status, delivery.NextAttemptAt })
            .HasDatabaseName("ix_github_webhook_deliveries_status_next_attempt");
        builder.HasIndex(delivery => delivery.ReceivedAt)
            .HasDatabaseName("ix_github_webhook_deliveries_received_at");
    }
}
