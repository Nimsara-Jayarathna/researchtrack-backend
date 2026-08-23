using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ResearchTrack.BuildingBlocks.Api.Configuration;

namespace ResearchTrack.SubmissionService.Persistence;

public sealed class SubmissionDbContextFactory : IDesignTimeDbContextFactory<SubmissionDbContext>
{
    public SubmissionDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SubmissionDbContext>();
        optionsBuilder.UseMySQL(
            DesignTimeDatabase.CreateConnectionString("researchtrack_submission_design"));

        return new SubmissionDbContext(optionsBuilder.Options);
    }
}
