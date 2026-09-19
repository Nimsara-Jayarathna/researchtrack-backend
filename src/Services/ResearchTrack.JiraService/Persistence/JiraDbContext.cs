using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Domain;
namespace ResearchTrack.JiraService.Persistence;
public sealed class JiraDbContext : DbContext
{
 public JiraDbContext(DbContextOptions<JiraDbContext> options):base(options){}
 public DbSet<JiraOAuthState> JiraOAuthStates=>Set<JiraOAuthState>();
 public DbSet<JiraOAuthSelection> JiraOAuthSelections=>Set<JiraOAuthSelection>();
 public DbSet<JiraConnection> JiraConnections=>Set<JiraConnection>();
 protected override void OnModelCreating(ModelBuilder b)
 {
  b.Entity<JiraOAuthState>(e=>{e.ToTable("jira_oauth_states");e.HasKey(x=>x.Id);e.Property(x=>x.StateHash).HasMaxLength(128).IsRequired();e.HasIndex(x=>x.StateHash).IsUnique();e.HasIndex(x=>new{x.ResearchProjectId,x.SupervisorUserId});});
  b.Entity<JiraOAuthSelection>(e=>{e.ToTable("jira_oauth_selections");e.HasKey(x=>x.Id);e.Property(x=>x.SelectionTokenHash).HasMaxLength(128).IsRequired();e.HasIndex(x=>x.SelectionTokenHash).IsUnique();e.Property(x=>x.AccessTokenProtected).HasColumnType("text");e.Property(x=>x.RefreshTokenProtected).HasColumnType("text");e.Property(x=>x.WorkspacesJson).HasColumnType("text");});
  b.Entity<JiraConnection>(e=>{e.ToTable("jira_connections");e.HasKey(x=>x.Id);e.HasIndex(x=>x.ResearchProjectId).IsUnique();e.Property(x=>x.CloudId).HasMaxLength(128).IsRequired();e.Property(x=>x.WorkspaceName).HasMaxLength(255).IsRequired();e.Property(x=>x.WorkspaceUrl).HasMaxLength(1024);e.Property(x=>x.JiraProjectId).HasMaxLength(128).IsRequired();e.Property(x=>x.JiraProjectKey).HasMaxLength(64).IsRequired();e.Property(x=>x.JiraProjectName).HasMaxLength(255).IsRequired();e.Property(x=>x.JiraBoardName).HasMaxLength(255);e.Property(x=>x.JiraBoardType).HasMaxLength(32);e.Property(x=>x.AccessTokenProtected).HasColumnType("text");e.Property(x=>x.RefreshTokenProtected).HasColumnType("text");e.Property(x=>x.SyncStatus).HasMaxLength(32).IsRequired();e.Property(x=>x.LastSyncError).HasColumnType("text");});
 }
}
