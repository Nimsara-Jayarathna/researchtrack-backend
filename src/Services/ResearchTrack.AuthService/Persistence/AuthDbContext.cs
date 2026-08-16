using Microsoft.EntityFrameworkCore;

namespace ResearchTrack.AuthService.Persistence;

public sealed class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options)
    {
    }

    // Sprint feature implementations add service-owned entity sets here.
}
