using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ResearchTrack.MeetingService.Persistence;

public sealed class MeetingDbContextFactory : IDesignTimeDbContextFactory<MeetingDbContext>
{
    public MeetingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection must be exported before running EF Core design-time commands. Use the repository migration scripts.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<MeetingDbContext>();
        optionsBuilder.UseMySQL(connectionString);
        return new MeetingDbContext(optionsBuilder.Options);
    }
}
