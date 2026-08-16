using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ResearchTrack.SubmissionService.Persistence;

public sealed class SubmissionDbContextFactory : IDesignTimeDbContextFactory<SubmissionDbContext>
{
    public SubmissionDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection must be exported before running EF Core design-time commands. Use the repository migration scripts.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<SubmissionDbContext>();
        optionsBuilder.UseMySQL(connectionString);
        return new SubmissionDbContext(optionsBuilder.Options);
    }
}
