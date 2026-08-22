using Microsoft.EntityFrameworkCore;
using ResearchTrack.AuthService.Domain;

namespace ResearchTrack.AuthService.Persistence;

public sealed class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<RegistrationSession> RegistrationSessions => Set<RegistrationSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
    }
}
