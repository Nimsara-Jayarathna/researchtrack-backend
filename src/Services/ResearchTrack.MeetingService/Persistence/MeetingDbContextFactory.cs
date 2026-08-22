using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ResearchTrack.BuildingBlocks.Api.Configuration;

namespace ResearchTrack.MeetingService.Persistence;

public sealed class MeetingDbContextFactory : IDesignTimeDbContextFactory<MeetingDbContext>
{
    public MeetingDbContext CreateDbContext(string[] args)
    {
        var connectionString = DatabaseConnectionStringResolver.ResolveFromEnvironment();

        var optionsBuilder = new DbContextOptionsBuilder<MeetingDbContext>();
        optionsBuilder.UseMySQL(connectionString);
        return new MeetingDbContext(optionsBuilder.Options);
    }
}
