# Documentation

Keep `README.md`, `CONTRIBUTING.md`, and `SECURITY.md` at the repository root. Keep deployment, script, configuration, and test-package READMEs beside the files they describe.

Put other Markdown documents under `docs/`, grouped by subject. Use `submission/` and `meeting/` for their feature plans, workflow guides, implementation summaries, and feature-specific testing instructions. Use `api/` for API contracts, `devops/` for deployment and workflow, `architecture/` for system design, `testing/` for general testing, and `features/` or `stories/` for product behavior. Keep existing filenames when moving documents, and update relative links.

## Api

- [API conventions](api/conventions.md)

## Architecture

- [Public GitHub repository linking](architecture/GITHUB_PUBLIC_LINKING_DESIGN.md)
- [Jira webhook + reconciliation synchronization](architecture/JIRA-WEBHOOK-RECONCILIATION.md)
- [ResearchTrack backend architecture](architecture/overview.md)
- [Repository map and ownership](architecture/repository-map.md)

## Database

- [EF Core migrations](database/migrations.md)

## Development

- [Command reference](development/commands.md)
- [Dependency baseline](development/dependencies.md)
- [First-run checklist](development/first-run-checklist.md)
- [Local setup](development/local-setup.md)
- [Secrets and runtime configuration](development/secrets.md)
- [Troubleshooting](development/troubleshooting.md)
- [US-102 — Secure Login & Role-Based Access](development/us-102-secure-login-rbac.md)

## Devops

- [Networking and Security Plan](devops/azure-production/01_NETWORKING_AND_SECURITY.md)
- [Infrastructure VM Plan](devops/azure-production/02_INFRASTRUCTURE_VM.md)
- [Infrastructure as Code and Bootstrap](devops/azure-production/03_IAC_AND_BOOTSTRAP.md)
- [Production CI/CD Plan](devops/azure-production/04_PRODUCTION_CICD.md)
- [First Deployment, Cutover and Validation](devops/azure-production/05_CUTOVER_AND_VALIDATION.md)
- [Azure Production — Frozen Decisions](devops/azure-production/DECISIONS.md)
- [Azure Production Implementation Status](devops/azure-production/IMPLEMENTATION_STATUS.md)
- [ResearchTrack Azure Production Deployment — Master Implementation Plan](devops/azure-production/MASTER_PLAN.md)
- [Azure Production Documentation Index](devops/azure-production/README.md)
- [Azure Production Runbook](devops/azure-production/RUNBOOK.md)
- [Production Runtime State-Preservation](devops/azure-runtime/README.md)
- [External Integrations and DNS](devops/configuration/EXTERNAL_INTEGRATIONS.md)
- [First Production Deployment Checklist](devops/configuration/FIRST_DEPLOYMENT_CHECKLIST.md)
- [GitHub Environments, Secrets and Variables](devops/configuration/GITHUB_ENVIRONMENTS.md)
- [Jira stateless OAuth token protection fix](devops/configuration/JIRA-STATELESS-TOKEN-FIX.md)
- [Configuring the GitHub `production` Environment (Azure)](devops/configuration/PRODUCTION_AZURE.md)
- [Deployment Configuration Guide](devops/configuration/README.md)
- [Service Configuration Bundles](devops/configuration/SERVICE_CONFIGURATION.md)
- [Test Deployment (`develop` → VPS + Docker Compose)](devops/configuration/TEST_VPS.md)

## Meeting

- [ResearchTrack Sprint 4 — Meeting Management](meeting/ResearchTrack_Sprint4_Meetings_Backend_Implementation_Plan.md)

## Stories

- [Jira issue hierarchy implementation](stories/JIRA-HIERARCHY-IMPLEMENTATION.md)
- [Jira webhook stability hardening](stories/JIRA-WEBHOOK-STABILITY-FIX.md)
- [SCRUM-15 — Connect an authorized GitHub repository through the ResearchTrack GitHub App](stories/SCRUM-15-connect-authorized-github-app.md)
- [SCRUM-179 issue detail restoration](stories/SCRUM-179-ISSUE-DESCRIPTION-RESTORE.md)
- [SCRUM-179 sprint discovery and Jira UI consistency fixes](stories/SCRUM-179-SPRINT-DISCOVERY-UI-FIXES.md)
- [US-103 Create Research Project — Full Creation Flow](stories/US-103-create-research-project-full-flow.md)
- [US-103 Create Research Project](stories/US-103-create-research-project.md)
- [US-106 — View Supervisor Dashboard](stories/US-106-view-supervisor-dashboard.md)
- [US-107 — View Student Dashboard](stories/US-107-view-student-dashboard.md)
- [Owner-granted GitHub App access request](stories/owner-granted-github-repository-access.md)

## Submission

- [SCRUM-70 — Submission Responsibility Final Implementation](submission/SCRUM-70_SUBMISSION_RESPONSIBILITY_FINAL_SUMMARY.md)
- [ResearchTrack_Sprint4_Submissions_Backend_Implementation_Plan](submission/ResearchTrack_Sprint4_Submissions_Backend_Implementation_Plan.md)
- [ResearchTrack submission review and revision workflow](submission/ResearchTrack_Submission_Review_Workflow.md)
- [ResearchTrack Submission Workflow UX + Review Update](submission/SUBMISSION_REVIEW_RESUBMISSION_IMPLEMENTATION_SUMMARY.md)
- [Submission S3 Foundation](submission/submission-s3-foundation.md)

## Testing

- [Sprint 3 Jira automated testing](testing/sprint-3-jira-automated-testing.md)
- [Test strategy](testing/test-strategy.md)
