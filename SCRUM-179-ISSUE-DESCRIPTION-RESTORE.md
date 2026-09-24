# SCRUM-179 issue detail restoration

- Keeps the sprint-board discovery/root-cause synchronization fixes.
- Adds the synchronized Jira `DescriptionJson`, Jira created timestamp, and existing metadata to the issue read contract.
- The issue list remains a local-database read; opening details does not call Jira.
