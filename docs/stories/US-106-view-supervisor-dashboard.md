# US-106 — View Supervisor Dashboard

## Public API

`GET /api/v1/supervisor/dashboard`

The Gateway routes this endpoint to Project Service. The dashboard is a dedicated Supervisor read model and is intentionally separate from the project collection API (`GET /api/v1/projects`).

## Authorization

- Requires an authenticated `SUPERVISOR`.
- The authenticated JWT subject is the Supervisor identity used by the query.
- Project Service filters at the database query to `Project.SupervisorUserId == current Supervisor user id`.
- Students receive `403 Forbidden` and projects owned by other Supervisors never enter the dashboard read model.

## Response model

The response preserves the Supervisor dashboard aggregate contract:

- lifecycle counts: total, planning, active, at risk, behind, completed;
- upcoming milestone count for non-completed projects due from today through the next 14 days;
- project health rows containing project summary, lifecycle state, primary milestone, last activity, progress and member count;
- recent projects ordered by latest project activity (maximum five);
- Jira dashboard indicators represented as `NOT_CONNECTED` with zero Jira risk counts until the Jira dashboard projection is implemented.

The endpoint returns `200 OK` with zero counts and empty arrays when a Supervisor has no projects. It does not return `404` for an empty dashboard.

## Frontend contract

The Supervisor frontend retains the existing flow:

`SupervisorDashboardPage -> useSupervisorDashboard -> supervisorDashboardApi -> /api/v1/supervisor/dashboard`

The backend owns aggregate statistics. The frontend continues to derive presentation-only attention ranking and upcoming-project lists from `projects[]`.

## Integration reliability

US-106 does not synchronously call GitHub or Jira services. Missing integrations are represented explicitly rather than making the dashboard unavailable. Later stories may enrich this dashboard read model without changing its public purpose.
