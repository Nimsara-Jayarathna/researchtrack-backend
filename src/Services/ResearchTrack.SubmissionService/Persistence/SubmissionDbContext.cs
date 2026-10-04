using Microsoft.EntityFrameworkCore;
using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.Persistence;

public sealed class SubmissionDbContext : DbContext
{
    public SubmissionDbContext(DbContextOptions<SubmissionDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubmissionRequirement> SubmissionRequirements => Set<SubmissionRequirement>();
    public DbSet<ResearchSubmission> ResearchSubmissions => Set<ResearchSubmission>();
    public DbSet<SubmissionVersion> SubmissionVersions => Set<SubmissionVersion>();
    public DbSet<SubmissionUploadSession> SubmissionUploadSessions => Set<SubmissionUploadSession>();
    public DbSet<SubmissionReview> SubmissionReviews => Set<SubmissionReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SubmissionDbContext).Assembly);
    }
}
