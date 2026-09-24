using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace ResearchTrack.JiraService.Persistence.Migrations;
[DbContext(typeof(JiraDbContext))]
[Migration("20260920070000_AddJiraIssueSynchronization")]
public sealed class AddJiraIssueSynchronization:Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.Sql(@"CREATE TABLE jira_issues (Id char(36) NOT NULL, JiraConnectionId char(36) NOT NULL, ResearchProjectId char(36) NOT NULL, JiraIssueId varchar(128) NOT NULL, IssueKey varchar(64) NOT NULL, Summary varchar(1024) NOT NULL, DescriptionJson longtext NULL, IssueTypeId longtext NULL, IssueTypeName varchar(128) NOT NULL, IsSubtask tinyint(1) NOT NULL, StatusId longtext NULL, StatusName varchar(128) NOT NULL, StatusCategoryId longtext NULL, StatusCategoryKey varchar(64) NULL, StatusCategoryName longtext NULL, PriorityId longtext NULL, PriorityName varchar(128) NULL, AssigneeAccountId longtext NULL, AssigneeDisplayName varchar(255) NULL, ReporterAccountId longtext NULL, ReporterDisplayName varchar(255) NULL, StoryPoints decimal(12,2) NULL, OriginalEstimateSeconds bigint NULL, RemainingEstimateSeconds bigint NULL, TimeSpentSeconds bigint NULL, ParentIssueId longtext NULL, ParentIssueKey longtext NULL, ResolutionId longtext NULL, ResolutionName longtext NULL, DueDate datetime NULL, ResolutionDate datetime NULL, JiraCreatedAt datetime NULL, JiraUpdatedAt datetime NULL, SyncedAt datetime NOT NULL, PRIMARY KEY (Id), UNIQUE INDEX IX_jira_issues_project_issue (ResearchProjectId,JiraIssueId), INDEX IX_jira_issues_project (ResearchProjectId), INDEX IX_jira_issues_project_status (ResearchProjectId,StatusCategoryKey), INDEX IX_jira_issues_project_key (ResearchProjectId,IssueKey), INDEX IX_jira_issues_connection (JiraConnectionId)) CHARACTER SET=utf8mb4;");
  m.Sql(@"CREATE TABLE jira_sprints (Id char(36) NOT NULL, JiraConnectionId char(36) NOT NULL, ResearchProjectId char(36) NOT NULL, JiraSprintId bigint NOT NULL, JiraBoardId bigint NOT NULL, Name varchar(255) NOT NULL, State varchar(32) NOT NULL, Goal varchar(2048) NULL, StartDate datetime NULL, EndDate datetime NULL, CompleteDate datetime NULL, SyncedAt datetime NOT NULL, PRIMARY KEY (Id), UNIQUE INDEX IX_jira_sprints_project_sprint (ResearchProjectId,JiraSprintId), INDEX IX_jira_sprints_connection (JiraConnectionId)) CHARACTER SET=utf8mb4;");
  m.Sql(@"CREATE TABLE jira_issue_sprints (JiraIssueId char(36) NOT NULL, JiraSprintId char(36) NOT NULL, PRIMARY KEY (JiraIssueId,JiraSprintId), INDEX IX_jira_issue_sprints_sprint (JiraSprintId)) CHARACTER SET=utf8mb4;");
 }
 protected override void Down(MigrationBuilder m){m.Sql("DROP TABLE IF EXISTS jira_issue_sprints;");m.Sql("DROP TABLE IF EXISTS jira_sprints;");m.Sql("DROP TABLE IF EXISTS jira_issues;");}
}
