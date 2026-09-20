using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Domain;
namespace ResearchTrack.JiraService.Persistence;
public sealed class JiraDbContext : DbContext
{
 public JiraDbContext(DbContextOptions<JiraDbContext> options):base(options){}
 public DbSet<JiraOAuthState> JiraOAuthStates=>Set<JiraOAuthState>();
 public DbSet<JiraOAuthSelection> JiraOAuthSelections=>Set<JiraOAuthSelection>();
 public DbSet<JiraConnection> JiraConnections=>Set<JiraConnection>();
 public DbSet<JiraIssue> JiraIssues=>Set<JiraIssue>();
 public DbSet<JiraSprint> JiraSprints=>Set<JiraSprint>();
 public DbSet<JiraIssueSprint> JiraIssueSprints=>Set<JiraIssueSprint>();
 protected override void OnModelCreating(ModelBuilder b)
 {
  b.Entity<JiraOAuthState>(e=>{e.ToTable("jira_oauth_states");e.HasKey(x=>x.Id);e.Property(x=>x.StateHash).HasMaxLength(128).IsRequired();e.HasIndex(x=>x.StateHash).IsUnique();e.HasIndex(x=>new{x.ResearchProjectId,x.SupervisorUserId});});
  b.Entity<JiraOAuthSelection>(e=>{e.ToTable("jira_oauth_selections");e.HasKey(x=>x.Id);e.Property(x=>x.SelectionTokenHash).HasMaxLength(128).IsRequired();e.HasIndex(x=>x.SelectionTokenHash).IsUnique();e.Property(x=>x.AccessTokenProtected).HasColumnType("text");e.Property(x=>x.RefreshTokenProtected).HasColumnType("text");e.Property(x=>x.WorkspacesJson).HasColumnType("text");});
  b.Entity<JiraConnection>(e=>{e.ToTable("jira_connections");e.HasKey(x=>x.Id);e.HasIndex(x=>x.ResearchProjectId).IsUnique();e.Property(x=>x.CloudId).HasMaxLength(128).IsRequired();e.Property(x=>x.WorkspaceName).HasMaxLength(255).IsRequired();e.Property(x=>x.WorkspaceUrl).HasMaxLength(1024);e.Property(x=>x.JiraProjectId).HasMaxLength(128).IsRequired();e.Property(x=>x.JiraProjectKey).HasMaxLength(64).IsRequired();e.Property(x=>x.JiraProjectName).HasMaxLength(255).IsRequired();e.Property(x=>x.JiraBoardName).HasMaxLength(255);e.Property(x=>x.JiraBoardType).HasMaxLength(32);e.Property(x=>x.AccessTokenProtected).HasColumnType("text");e.Property(x=>x.RefreshTokenProtected).HasColumnType("text");e.Property(x=>x.SyncStatus).HasMaxLength(32).IsRequired();e.Property(x=>x.LastSyncError).HasColumnType("text");});
  b.Entity<JiraIssue>(e=>{e.ToTable("jira_issues");e.HasKey(x=>x.Id);e.HasIndex(x=>new{x.ResearchProjectId,x.JiraIssueId}).IsUnique();e.HasIndex(x=>x.ResearchProjectId);e.HasIndex(x=>new{x.ResearchProjectId,x.StatusCategoryKey});e.HasIndex(x=>new{x.ResearchProjectId,x.IssueKey});e.HasIndex(x=>x.JiraConnectionId);e.Property(x=>x.JiraIssueId).HasMaxLength(128).IsRequired();e.Property(x=>x.IssueKey).HasMaxLength(64).IsRequired();e.Property(x=>x.Summary).HasMaxLength(1024).IsRequired();e.Property(x=>x.DescriptionJson).HasColumnType("longtext");e.Property(x=>x.IssueTypeName).HasMaxLength(128).IsRequired();e.Property(x=>x.StatusName).HasMaxLength(128).IsRequired();e.Property(x=>x.StatusCategoryKey).HasMaxLength(64);e.Property(x=>x.PriorityName).HasMaxLength(128);e.Property(x=>x.AssigneeDisplayName).HasMaxLength(255);e.Property(x=>x.ReporterDisplayName).HasMaxLength(255);e.Property(x=>x.StoryPoints).HasPrecision(12,2);});
  b.Entity<JiraSprint>(e=>{e.ToTable("jira_sprints");e.HasKey(x=>x.Id);e.HasIndex(x=>new{x.ResearchProjectId,x.JiraSprintId}).IsUnique();e.HasIndex(x=>x.JiraConnectionId);e.Property(x=>x.Name).HasMaxLength(255).IsRequired();e.Property(x=>x.State).HasMaxLength(32).IsRequired();e.Property(x=>x.Goal).HasMaxLength(2048);});
  b.Entity<JiraIssueSprint>(e=>{e.ToTable("jira_issue_sprints");e.HasKey(x=>new{x.JiraIssueId,x.JiraSprintId});e.HasIndex(x=>x.JiraSprintId);});
 }
}
