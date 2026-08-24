using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ResearchTrack.BuildingBlocks.Api.Configuration;

namespace ResearchTrack.MeetingService.Persistence;

public sealed class MeetingDbContextFactory : IDesignTimeDbContextFactory<MeetingDbContext>
{
    public MeetingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MeetingDbContext>();
        optionsBuilder.UseMySQL(
            DesignTimeDatabase.CreateConnectionString("researchtrack_meeting_design"));

        return new MeetingDbContext(optionsBuilder.Options);
    }
}
