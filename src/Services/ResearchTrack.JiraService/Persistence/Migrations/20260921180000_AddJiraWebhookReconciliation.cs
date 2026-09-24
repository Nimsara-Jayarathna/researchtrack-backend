using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable
namespace ResearchTrack.JiraService.Persistence.Migrations;

[DbContext(typeof(JiraDbContext))]
[Migration("20260921180000_AddJiraWebhookReconciliation")]
public partial class AddJiraWebhookReconciliation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name:"WebhookStatus",table:"jira_connections",type:"varchar(32)",maxLength:32,nullable:false,defaultValue:"NOT_REGISTERED");
        migrationBuilder.AddColumn<long>(name:"WebhookId",table:"jira_connections",type:"bigint",nullable:true);
        migrationBuilder.AddColumn<DateTimeOffset>(name:"WebhookExpiresAt",table:"jira_connections",type:"datetime(6)",nullable:true);
        migrationBuilder.AddColumn<DateTimeOffset>(name:"LastWebhookAt",table:"jira_connections",type:"datetime(6)",nullable:true);
        migrationBuilder.AddColumn<DateTimeOffset>(name:"LastReconciledAt",table:"jira_connections",type:"datetime(6)",nullable:true);
        migrationBuilder.CreateTable(name:"jira_sync_jobs",columns:table=>new{Id=table.Column<Guid>(type:"char(36)",nullable:false),ResearchProjectId=table.Column<Guid>(type:"char(36)",nullable:false),Reason=table.Column<string>(type:"varchar(64)",maxLength:64,nullable:false),Scope=table.Column<string>(type:"varchar(32)",maxLength:32,nullable:false),EntityId=table.Column<string>(type:"varchar(128)",maxLength:128,nullable:true),Status=table.Column<string>(type:"varchar(32)",maxLength:32,nullable:false),AttemptCount=table.Column<int>(type:"int",nullable:false),RequestedAt=table.Column<DateTimeOffset>(type:"datetime(6)",nullable:false),AvailableAt=table.Column<DateTimeOffset>(type:"datetime(6)",nullable:false),StartedAt=table.Column<DateTimeOffset>(type:"datetime(6)",nullable:true),CompletedAt=table.Column<DateTimeOffset>(type:"datetime(6)",nullable:true),LastError=table.Column<string>(type:"text",nullable:true)},constraints:table=>table.PrimaryKey("PK_jira_sync_jobs",x=>x.Id));
        migrationBuilder.CreateTable(name:"jira_webhook_events",columns:table=>new{Id=table.Column<Guid>(type:"char(36)",nullable:false),DeliveryId=table.Column<string>(type:"varchar(255)",maxLength:255,nullable:false),CloudId=table.Column<string>(type:"varchar(128)",maxLength:128,nullable:false),ResearchProjectId=table.Column<Guid>(type:"char(36)",nullable:true),EventType=table.Column<string>(type:"varchar(128)",maxLength:128,nullable:false),JiraIssueId=table.Column<string>(type:"varchar(128)",maxLength:128,nullable:true),IssueKey=table.Column<string>(type:"varchar(64)",maxLength:64,nullable:true),PayloadJson=table.Column<string>(type:"longtext",nullable:false),Status=table.Column<string>(type:"varchar(32)",maxLength:32,nullable:false),AttemptCount=table.Column<int>(type:"int",nullable:false),LastError=table.Column<string>(type:"text",nullable:true),ReceivedAt=table.Column<DateTimeOffset>(type:"datetime(6)",nullable:false),ProcessedAt=table.Column<DateTimeOffset>(type:"datetime(6)",nullable:true)},constraints:table=>table.PrimaryKey("PK_jira_webhook_events",x=>x.Id));
        migrationBuilder.CreateIndex(name:"IX_jira_sync_jobs_Status_AvailableAt",table:"jira_sync_jobs",columns:new[]{"Status","AvailableAt"});
        migrationBuilder.CreateIndex(name:"IX_jira_sync_jobs_ResearchProjectId_Status",table:"jira_sync_jobs",columns:new[]{"ResearchProjectId","Status"});
        migrationBuilder.CreateIndex(name:"IX_jira_webhook_events_DeliveryId",table:"jira_webhook_events",column:"DeliveryId",unique:true);
        migrationBuilder.CreateIndex(name:"IX_jira_webhook_events_Status_ReceivedAt",table:"jira_webhook_events",columns:new[]{"Status","ReceivedAt"});
        migrationBuilder.CreateIndex(name:"IX_jira_webhook_events_ResearchProjectId",table:"jira_webhook_events",column:"ResearchProjectId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name:"jira_sync_jobs"); migrationBuilder.DropTable(name:"jira_webhook_events");
        migrationBuilder.DropColumn(name:"WebhookStatus",table:"jira_connections"); migrationBuilder.DropColumn(name:"WebhookId",table:"jira_connections"); migrationBuilder.DropColumn(name:"WebhookExpiresAt",table:"jira_connections"); migrationBuilder.DropColumn(name:"LastWebhookAt",table:"jira_connections"); migrationBuilder.DropColumn(name:"LastReconciledAt",table:"jira_connections");
    }
}
