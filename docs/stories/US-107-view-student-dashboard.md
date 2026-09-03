# US-107 — View Student Dashboard

## Story

As a Student, the Sprint 1 student home presents the Research Projects to which the authenticated Student is currently assigned and provides direct navigation into each project workspace.

## Architecture Decision

US-107 intentionally reuses the canonical Project Service read model instead of introducing a duplicate Student Dashboard controller/service. Supervisor and Student project collection reads share `GET /api/v1/projects`; `ProjectService.GetAccessibleProjectsAsync` applies the role-specific authorization query.

For a Student, a project is returned only when `project_members` contains an active `STUDENT` membership for the authenticated user. Project details use `GET /api/v1/projects/{projectId}` and the same membership boundary.

This keeps Sprint 1 authorization in one backend source of truth while the frontend `/student/projects` route acts as the Student dashboard/home surface.

## Sprint 1 Behavior

- Assigned projects are returned from `GET /api/v1/projects`.
- An unrelated Student receives no project in the collection.
- A Student with no memberships receives an empty collection (`200`, not `404`).
- Project detail is available only while the Student remains a member.
- Removing a Student from a project removes both detail access and the project from subsequent Student collection reads.
- The frontend revalidates the collection when the Student home is entered and revalidates project detail when its route is entered, avoiding long-lived membership-dependent UI caches.

## UI Contract

The Student project card remains a compact navigation summary showing project title, summary, lifecycle status, progress, Supervisor, batch, semester, milestone and last activity. Long title/summary/Supervisor values are constrained so one record cannot distort the project grid.

Project detail responses order the Supervisor membership before Student memberships, and the project Team view preserves that hierarchy defensively on the client. The project leader remains domain data, but the generic member cards do not repeat a separate leader badge; leader management remains a Supervisor-side responsibility.

## Regression Coverage

`ProjectIntegrationTests` verifies:

- assigned and unrelated Student collection visibility;
- empty collection for a Student with no memberships;
- owner/assigned-Student project detail authorization;
- removal revokes detail access; and
- removal also removes the project from subsequent Student collection reads.
