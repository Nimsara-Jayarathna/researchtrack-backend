# US-103 Create Research Project

Implemented story scope:

- `POST /api/v1/projects` — authenticated Supervisor only.
- `GET /api/v1/projects` — only projects owned by the authenticated Supervisor.
- `GET /api/v1/projects/{projectId}` — owner-scoped basic read view.
- Ownership always comes from JWT `sub`; the request cannot choose a Supervisor id.
- Students are denied creation by the shared `SupervisorOnly` policy.
- Required fields: title, summary, batch, semester.
- Limits: title 40, summary 250, batch 32, semester 32.
- Initial state: `PLANNING`, progress `0`.
- No cross-service database foreign key to Auth; `SupervisorUserId` is a scalar service-boundary reference.
- Student membership, leader assignment, milestones, editing, dashboards, GitHub, Jira, meetings, submissions and files remain for their owning stories.

The Supervisor project collection and minimal read view are included because AC4 requires the newly-created project to be visible in the Supervisor workspace.
