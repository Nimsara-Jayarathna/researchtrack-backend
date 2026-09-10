using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence.Configurations;

public sealed class GitHubRepositoryConfiguration : IEntityTypeConfiguration<GitHubRepository>
{
    public void Configure(EntityTypeBuilder<GitHubRepository> builder)
    {
        builder.ToTable("github_repositories");
        builder.HasKey(repository => repository.Id);
        builder.Property(repository => repository.Id).ValueGeneratedNever();
        builder.Property(repository => repository.GitHubRepositoryId).IsRequired();
        builder.Property(repository => repository.FullName).HasMaxLength(512).IsRequired();
        builder.Property(repository => repository.Name).HasMaxLength(255).IsRequired();
        builder.Property(repository => repository.OwnerLogin).HasMaxLength(255).IsRequired();
        builder.Property(repository => repository.DefaultBranch).HasMaxLength(255);
        builder.Property(repository => repository.Url).HasMaxLength(2048).IsRequired();
        builder.Property(repository => repository.CreatedAt).IsRequired();
        builder.Property(repository => repository.UpdatedAt).IsRequired();

        builder.HasIndex(repository => new { repository.SourceId, repository.GitHubRepositoryId })
            .IsUnique()
            .HasDatabaseName("ux_github_repositories_source_github_id");
        builder.HasIndex(repository => repository.GitHubRepositoryId)
            .HasDatabaseName("ix_github_repositories_github_id");

        builder.HasOne<GitHubAccessSource>()
            .WithMany()
            .HasForeignKey(repository => repository.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
