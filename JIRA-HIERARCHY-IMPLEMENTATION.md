# Jira issue hierarchy implementation

The issue API now exposes `isSubtask` together with the already synchronized `parentIssueKey`. No database migration is required: `JiraIssue` already stores Jira issue type, subtask flag, parent issue id and parent issue key.

The frontend builds the hierarchy only from Jira's synchronized parent relationship. It never infers ancestry from issue keys, names, ordering, or issue type. Missing parents remain visible as root/orphan rows. Epic, Story, Task, Bug and Subtask labels are presentation metadata; parent linkage is authoritative.

The hierarchy UI supports expand/collapse, expand/collapse all, ancestry-preserving search, child counts, missing-parent context, and the existing status/priority/assignee columns. Student and Supervisor use the same shared Jira issue component.
